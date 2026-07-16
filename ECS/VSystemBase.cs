namespace BattleLuck.ECS;

/// <summary>
/// Describes how a system constructs its primary <see cref="EntityQuery"/> definition.
/// </summary>
public interface IQuerySpec
{
    /// <summary>
    /// Configures the provided builder with the query's component and option requirements.
    /// </summary>
    /// <param name="builder">Builder that collects the query definition.</param>
    void Build(ref EntityQueryBuilder builder);

    /// <summary>
    /// Indicates whether the generated query must be required for update.
    /// </summary>
    bool RequireForUpdate => true;
}

/// <summary>
/// Provides a registration surface for per-frame refresh actions.
/// </summary>
public interface IRegistrar
{
    /// <summary>
    /// Registers a refresh action that runs at the beginning of each update.
    /// </summary>
    /// <param name="refreshAction">Action invoked before the work executes.</param>
    void Register(Action<SystemBase> refreshAction);
}

/// <summary>
/// Supplies contextual information to work instances during lifecycle events.
/// </summary>
public readonly struct SystemContext
{
    readonly Action<EntityQuery, Action<NativeArray<Entity>>> _withTempEntities;
    readonly Action<EntityQuery, Action<Entity>> _forEachEntity;
    readonly Action<EntityQuery, Action<NativeArray<ArchetypeChunk>>> _withTempChunks;
    readonly Action<EntityQuery, Action<ArchetypeChunk>> _forEachChunk;
    readonly Func<Entity, bool> _exists;

    public SystemContext(
        SystemBase system,
        EntityManager entityManager,
        EntityQuery query,
        EntityTypeHandle entityTypeHandle,
        EntityStorageInfoLookup entityStorageInfoLookup,
        IRegistrar registrar,
        Action<EntityQuery, Action<NativeArray<Entity>>> withTempEntities,
        Action<EntityQuery, Action<Entity>> forEachEntity,
        Action<EntityQuery, Action<NativeArray<ArchetypeChunk>>> withTempChunks,
        Action<EntityQuery, Action<ArchetypeChunk>> forEachChunk,
        Func<Entity, bool> exists)
    {
        System = system ?? throw new ArgumentNullException(nameof(system));
        EntityManager = entityManager;
        Query = query;
        EntityTypeHandle = entityTypeHandle;
        EntityStorageInfoLookup = entityStorageInfoLookup;
        Registrar = registrar ?? throw new ArgumentNullException(nameof(registrar));
        _withTempEntities = withTempEntities ?? throw new ArgumentNullException(nameof(withTempEntities));
        _forEachEntity = forEachEntity ?? throw new ArgumentNullException(nameof(forEachEntity));
        _withTempChunks = withTempChunks ?? throw new ArgumentNullException(nameof(withTempChunks));
        _forEachChunk = forEachChunk ?? throw new ArgumentNullException(nameof(forEachChunk));
        _exists = exists ?? throw new ArgumentNullException(nameof(exists));
    }

    /// <summary>Gets the executing <see cref="SystemBase"/> instance.</summary>
    public SystemBase System { get; }

    /// <summary>Gets the world-level <see cref="EntityManager"/>.</summary>
    public EntityManager EntityManager { get; }

    /// <summary>Gets the primary <see cref="EntityQuery"/> constructed for the system.</summary>
    public EntityQuery Query { get; }

    /// <summary>Gets the cached <see cref="EntityTypeHandle"/>.</summary>
    public EntityTypeHandle EntityTypeHandle { get; }

    /// <summary>Gets the cached <see cref="EntityStorageInfoLookup"/>.</summary>
    public EntityStorageInfoLookup EntityStorageInfoLookup { get; }

    /// <summary>Gets the registrar used to schedule per-update refresh actions.</summary>
    public IRegistrar Registrar { get; }

    /// <summary>Executes an action with a temporary entity array for the specified query.</summary>
    public void WithTempEntities(EntityQuery query, Action<NativeArray<Entity>> action) =>
        _withTempEntities(query, action);

    /// <summary>Iterates each entity in the specified query.</summary>
    public void ForEachEntity(EntityQuery query, Action<Entity> action) =>
        _forEachEntity(query, action);

    /// <summary>Executes an action with a temporary chunk array for the specified query.</summary>
    public void WithTempChunks(EntityQuery query, Action<NativeArray<ArchetypeChunk>> action) =>
        _withTempChunks(query, action);

    /// <summary>Iterates each chunk in the specified query.</summary>
    public void ForEachChunk(EntityQuery query, Action<ArchetypeChunk> action) =>
        _forEachChunk(query, action);

    /// <summary>Determines whether the entity currently exists.</summary>
    public bool Exists(Entity entity) => _exists(entity);
}

/// <summary>
/// Defines the lifecycle hooks executed by <see cref="VSystemBase{TWork}"/> implementations.
/// </summary>
public interface ISystemWork : IQuerySpec
{
    /// <summary>Invoked when the owning system is created.</summary>
    void OnCreate(SystemContext context) { }

    /// <summary>Invoked when the owning system starts running.</summary>
    void OnStartRunning(SystemContext context) { }

    /// <summary>Invoked at the beginning of each update cycle.</summary>
    void OnUpdate(SystemContext context) { }

    /// <summary>Invoked when the owning system stops running.</summary>
    void OnStopRunning(SystemContext context) { }

    /// <summary>Invoked when the owning system is destroyed.</summary>
    void OnDestroy(SystemContext context) { }
}

/// <summary>
/// Provides a reusable base implementation for DOTS <see cref="SystemBase"/> workflows that delegate
/// their behavior to strongly typed work objects.
/// </summary>
/// <typeparam name="TWork">Work definition executed by the system.</typeparam>
public abstract class VSystemBase<TWork> : SystemBase, IRegistrar
    where TWork : class, ISystemWork, new()
{
    readonly List<Action<SystemBase>> _refreshActions = new();

    EntityTypeHandle _entityTypeHandle;
    EntityStorageInfoLookup _entityStorageInfoLookup;
    EntityQuery _query;

    protected TWork Work { get; private set; } = new();

    /// <summary>Gets the entity type handle refreshed each update.</summary>
    protected ref EntityTypeHandle EntityTypeHandle => ref _entityTypeHandle;

    /// <summary>Gets the entity storage info lookup refreshed each update.</summary>
    protected ref EntityStorageInfoLookup EntityStorageInfoLookup => ref _entityStorageInfoLookup;

    /// <summary>Gets the entity query backing the system.</summary>
    protected EntityQuery Query => _query;

    public override void OnCreate()
    {
        base.OnCreate();

        BuildQuery();

        if (Work.RequireForUpdate)
            RequireForUpdate(_query);

        RefreshEntityHandles();

        Work.OnCreate(CreateContext());
    }

    public override void OnStartRunning()
    {
        base.OnStartRunning();
        Work.OnStartRunning(CreateContext());
    }

    public override void OnStopRunning()
    {
        Work.OnStopRunning(CreateContext());
        base.OnStopRunning();
    }

    public override void OnDestroy()
    {
        Work.OnDestroy(CreateContext());
        _refreshActions.Clear();
        base.OnDestroy();
    }

    public override void OnUpdate()
    {
        RefreshEntityHandles();
        RunRefreshActions();
        Work.OnUpdate(CreateContext());
    }

    /// <summary>
    /// Executes the supplied callback with a temporary entity array for the provided query.
    /// </summary>
    protected void WithTempEntities(EntityQuery query, Action<NativeArray<Entity>> action)
    {
        if (query == default)
            throw new ArgumentNullException(nameof(query));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var entities = query.ToEntityArray(Allocator.Temp);
        try
        {
            action(entities);
        }
        finally
        {
            if (entities.IsCreated)
                entities.Dispose();
        }
    }

    /// <summary>
    /// Iterates every entity in the query using a temporary array allocation.
    /// </summary>
    protected void ForEachEntity(EntityQuery query, Action<Entity> action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        WithTempEntities(query, entities =>
        {
            for (int i = 0; i < entities.Length; ++i)
                action(entities[i]);
        });
    }

    /// <summary>
    /// Executes the supplied callback with a temporary archetype chunk array for the provided query.
    /// </summary>
    protected void WithTempChunks(EntityQuery query, Action<NativeArray<ArchetypeChunk>> action)
    {
        if (query == default)
            throw new ArgumentNullException(nameof(query));
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        var chunks = query.ToArchetypeChunkArray(Allocator.Temp);
        try
        {
            action(chunks);
        }
        finally
        {
            if (chunks.IsCreated)
                chunks.Dispose();
        }
    }

    /// <summary>
    /// Iterates every chunk in the query using a temporary array allocation.
    /// </summary>
    protected void ForEachChunk(EntityQuery query, Action<ArchetypeChunk> action)
    {
        if (action == null)
            throw new ArgumentNullException(nameof(action));

        WithTempChunks(query, chunks =>
        {
            for (int i = 0; i < chunks.Length; ++i)
                action(chunks[i]);
        });
    }

    /// <summary>
    /// Determines whether the entity still exists according to the latest storage lookup.
    /// </summary>
    protected new bool Exists(Entity entity) => _entityStorageInfoLookup.Exists(entity);

    void IRegistrar.Register(Action<SystemBase> refreshAction)
    {
        if (refreshAction == null)
            throw new ArgumentNullException(nameof(refreshAction));

        _refreshActions.Add(refreshAction);
    }

    void BuildQuery()
    {
        var builder = new EntityQueryBuilder(Allocator.Temp);
        try
        {
            Work.Build(ref builder);
            _query = EntityManager.CreateEntityQuery(ref builder);
        }
        finally
        {
            builder.Dispose();
        }
    }

    void RefreshEntityHandles()
    {
        _entityTypeHandle = GetEntityTypeHandle();
        _entityStorageInfoLookup = GetEntityStorageInfoLookup();
    }

    void RunRefreshActions()
    {
        if (_refreshActions.Count == 0)
            return;

        foreach (var action in _refreshActions)
            action(this);
    }

    SystemContext CreateContext() => new(
        this,
        EntityManager,
        _query,
        _entityTypeHandle,
        _entityStorageInfoLookup,
        this,
        WithTempEntities,
        ForEachEntity,
        WithTempChunks,
        ForEachChunk,
        Exists);
}
