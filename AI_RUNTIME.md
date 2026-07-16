# Docker AI Runtime

A lightweight FastAPI-based AI service with local LLM inference via Ollama.

## Features

- **FastAPI** backend with async support
- **Ollama** integration for local LLM inference
- **Streaming responses** via Server-Sent Events (SSE)
- **Embeddings** generation
- **Model management** (list, select)
- **Health checks** with automatic Ollama detection

## Quick Start

### 1. Build and Start

```bash
docker compose -f docker-compose.ai.yml up --build
```

### 2. Check Health

```bash
curl http://localhost:8000/health
```

### 3. Try a Query

```bash
curl -X POST http://localhost:8000/query \
  -H "Content-Type: application/json" \
  -d '{"text": "Hello, what is Docker?"}'
```

### 4. Stream a Response

```bash
curl "http://localhost:8000/query-stream?text=Tell%20me%20about%20containers"
```

### 5. List Available Models

```bash
curl http://localhost:8000/models
```

## API Endpoints

### GET `/health`
Health check and Ollama connection status.

**Response:**
```json
{
  "status": "ok",
  "ollama": "connected"
}
```

### POST `/query`
Send a query and get a complete response.

**Request:**
```json
{
  "text": "What is Docker?",
  "model": "llama2",
  "temperature": 0.7,
  "top_p": 0.9
}
```

**Response:**
```json
{
  "result": "Docker is a containerization platform...",
  "model": "llama2",
  "tokens": 42
}
```

### GET `/query-stream`
Stream a response token-by-token.

**Query Parameters:**
- `text` (required): The question
- `model` (optional): Model name (default: llama2)

**Response:** Server-Sent Events (SSE) stream

```bash
curl "http://localhost:8000/query-stream?text=hello"
# Returns: data: {"response": "Hello!", ...}\n\n
```

### POST `/embeddings`
Generate embeddings for text.

**Request:**
```json
{
  "text": "Some text to embed"
}
```

**Response:**
```json
{
  "embedding": [0.123, 0.456, ...],
  "model": "llama2"
}
```

### GET `/models`
List available models in Ollama.

**Response:**
```json
{
  "models": [
    {"name": "llama2:latest", "size": 3825213504},
    {"name": "mistral:latest", "size": 4109453312}
  ]
}
```

## Configuration

### Environment Variables

Set in `.env` or `docker-compose.ai.yml`:

- `LLAMA_API_BASE_URL`: Ollama/Llama API URL for the BattleLuck plugin (default: `http://localhost:11434`)
- `LLAMA_API_MODEL`: Default model to use for the BattleLuck plugin (default: `llama2`)
- `META_LLAMA_API_BASE_URL`: legacy alias for `LLAMA_API_BASE_URL`
- `META_LLAMA_MODEL`: legacy alias for `LLAMA_API_MODEL`
- `OLLAMA_HOST`: local Ollama daemon host for the runtime service (example: `0.0.0.0:11434`)
- `LLM_MODEL`: Docker runtime model default for the standalone AI service (default: `llama2`)

### Example `.env`

```env
LLAMA_API_MODEL=llama2
LLAMA_API_BASE_URL=http://localhost:11434
OLLAMA_HOST=0.0.0.0:11434
```
## Docker Compose Services

### `ollama`
- **Image:** `ollama/ollama:latest`
- **Port:** `11434:11434`
- **Volume:** `ollama_data` (model cache)
- **Healthcheck:** Queries `/api/tags` endpoint

### `ai-runtime`
- **Build:** `./ai_runtime/Dockerfile`
- **Port:** `8000:8000`
- **Depends on:** `ollama`
- **Healthcheck:** Queries `/health` endpoint

## Usage Examples

### Python Client

```python
import httpx

async with httpx.AsyncClient() as client:
    response = await client.post(
        "http://localhost:8000/query",
        json={"text": "What is containerization?"}
    )
    print(response.json()["result"])
```

### cURL Streaming

```bash
curl -N "http://localhost:8000/query-stream?text=explain%20Docker%20in%20one%20sentence"
```

### JavaScript/Node.js

```javascript
async function queryAI(text) {
  const response = await fetch('http://localhost:8000/query', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ text })
  });
  return response.json();
}

// Stream example
const eventSource = new EventSource(`http://localhost:8000/query-stream?text=${encodeURIComponent(text)}`);
eventSource.onmessage = (event) => {
  console.log(JSON.parse(event.data));
};
```

## Troubleshooting

### Ollama Connection Refused

```bash
docker compose -f docker-compose.ai.yml logs ollama
```

Ensure Ollama service is healthy:
```bash
curl http://localhost:11434/api/tags
```

### Model Not Found

Pull a model into Ollama:
```bash
docker exec ai-runtime-ollama ollama pull llama2
```

List available models:
```bash
curl http://localhost:8000/models
```

### High Latency on First Query

Models are loaded into memory on first use. Subsequent queries are faster. For GPU acceleration:

```yaml
services:
  ollama:
    deploy:
      resources:
        reservations:
          devices:
            - driver: nvidia
              count: all
              capabilities: [gpu]
```

## Next Steps

- Add authentication (API keys)
- Integrate with vector databases (Pinecone, Weaviate)
- Add RAG (Retrieval-Augmented Generation)
- Deploy to Kubernetes
- Add request rate limiting and caching

## Stop Services

```bash
docker compose -f docker-compose.ai.yml down
```

Remove volumes:
```bash
docker compose -f docker-compose.ai.yml down -v
```
