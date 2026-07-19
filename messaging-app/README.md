# Messaging App

A real-time messaging application with OpenAI-powered thread summarization, built with Supabase.

## Features

- **Users**: Authentication via Supabase Auth (GitHub OAuth)
- **Threads**: Create and manage conversation threads
- **Messages**: Send and receive messages in threads
- **Thread Summaries**: AI-powered summarization using OpenAI GPT models

## Project Structure

```
messaging-app/
├── schema.sql              # Database schema with RLS policies
├── .env.example            # Environment variables template
├── package.json            # Frontend dependencies
├── vite.config.ts          # Vite configuration
├── tsconfig.json           # TypeScript configuration
├── index.html              # Main HTML entry point
├── src/
│   ├── main.tsx            # React entry point
│   ├── App.tsx             # Main application component
│   ├── App.css             # Application styles
│   ├── index.css           # Global styles
│   └── components/
│       ├── ThreadList.tsx      # Thread list and creation
│       ├── MessageList.tsx     # Message display
│       ├── MessageInput.tsx    # Message input form
│       └── ThreadSummary.tsx   # AI summary display
└── functions/
    └── summarize-thread/
        └── index.ts            # Edge function for OpenAI summarization
```

## Setup

### 1. Database Setup

Run `schema.sql` in your Supabase SQL editor to create the tables:
- `threads` - Conversation threads
- `thread_members` - Thread membership (many-to-many)
- `messages` - Messages within threads
- `thread_summaries` - AI-generated summaries

### 2. Edge Function Setup

Deploy the edge function to Supabase:

```bash
# Install Supabase CLI
npm install -g supabase

# Login
supabase login

# Link your project
supabase link --project-ref your-project-ref

# Deploy the function
supabase functions deploy summarize-thread
```

Set the following environment variables in your Supabase dashboard:
- `OPENAI_API_KEY` - Your OpenAI API key
- `SUPABASE_URL` - Your Supabase project URL
- `SUPABASE_SERVICE_ROLE_KEY` - Your service role key

### 3. Frontend Setup

```bash
# Install dependencies
npm install

# Create .env file with your Supabase credentials
cp .env.example .env
# Edit .env with your values

# Run development server
npm run dev
```

## Database Schema

### Tables

- **threads**: Stores conversation threads with creator reference
- **thread_members**: Many-to-many relationship between users and threads
- **messages**: Messages with optional parent for replies
- **thread_summaries**: AI-generated summaries per thread

### Row Level Security

All tables have RLS enabled with policies:
- Thread members can read threads they belong to
- Thread creators can create threads
- Members can read/write messages in their threads
- Members can read/write summaries for their threads

## API

### Edge Function: `summarize-thread`

**POST** `/functions/v1/summarize-thread`

Request body:
```json
{
  "thread_id": "uuid",
  "model": "gpt-4o-mini" // optional, defaults to gpt-4o-mini
}
```

Response:
```json
{
  "success": true,
  "summary": {
    "thread_id": "uuid",
    "summary": "Thread summary text",
    "key_points": ["point 1", "point 2"],
    "model": "gpt-4o-mini",
    "created_at": "timestamp"
  }
}
```

## Usage

1. Sign in with GitHub
2. Create a new thread or select an existing one
3. Send messages in the thread
4. Click "Summarize Thread" to generate an AI summary
5. View the summary and key points below the messages