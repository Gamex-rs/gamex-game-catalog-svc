# Gamex — Game Catalog Service Specification

> **Repository:** `gamex-rs/gamex-game-catalog-svc`  
> **Version:** 1.0  
> **Date:** 2026-05-12

---

## 1. Purpose

The Game Catalog Service is the single source of truth for game metadata within Gamex. It owns the integration with the external RAWG API, caches all game data locally, and maintains aggregate statistics per game (how many users own it, have it on their wishlist, etc.). It exposes a read-heavy REST API consumed primarily by the frontend.

It also publishes `catalog.*` events to the `gamex-catalog-events` queue whenever game or platform data changes, so other services (currently the User Library Service) can keep a read-only local replica of the fields they need without calling this service synchronously.

---

## 2. Tech Stack

| Concern | Technology |
|---|---|
| Runtime | .NET 10 |
| Framework | ASP.NET Core Web API |
| ORM | Entity Framework Core 8 |
| Database | MySQL 8 (Aurora MySQL Serverless v2 on AWS, official Docker image locally) |
| SQS Consumer | AWSSDK.SQS + background `IHostedService` |
| SQS Publisher | AWSSDK.SQS (catalog events) |
| Image storage | AWS S3 (`gamex-media` bucket), served via Cloudflare CDN |
| Config / Secrets | AWS SSM Parameter Store + Secrets Manager (env vars locally) |

---

## 3. API Endpoints

Base path: `/api/v1`

All responses are JSON. Authentication is **not required** for read endpoints. Admin endpoints require a valid JWT with `role=admin` claim.

### 3.1 Games

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/games` | None | Paginated list; supports filters and sorting |
| `GET` | `/games/search?q={term}` | None | Autocomplete search by title (max 10 results) |
| `GET` | `/games/{id}` | None | Full game detail including aggregate stats |
| `POST` | `/games/sync` | Admin | Fetch a game from RAWG by slug and cache it locally |
| `PUT` | `/games/{id}` | Admin | Manually update game metadata |

#### `GET /games` — Query Parameters

| Parameter | Type | Description |
|---|---|---|
| `q` | string | Title search (partial match) |
| `platformIds` | GUID (repeatable) | Filter by one or more platforms — pass multiple times: `?platformIds=uuid1&platformIds=uuid2`. When the user is authenticated and has platform preferences set, the frontend automatically populates this parameter with their preferred platform IDs; the API applies no implicit filtering of its own. |
| `genreId` | GUID | Filter by genre |
| `sortBy` | string | `title`, `releaseDate`, `metacritic`, `ownedCount` |
| `sortDir` | string | `asc`, `desc` |
| `page` | int | Page number (1-based, default 1) |
| `pageSize` | int | Items per page (default 20, max 50) |

#### `GET /games/{id}` — Response

```json
{
  "id": "uuid",
  "title": "string",
  "description": "string",
  "coverImageUrl": "string",
  "releaseDate": "2024-11-12",
  "metacritic": 90,
  "rawgSlug": "string",
  "platforms": [{ "id": "uuid", "name": "PS5", "slug": "playstation5" }],
  "genres": [{ "id": "uuid", "name": "Action", "slug": "action" }],
  "stats": {
    "ownedCount": 142,
    "wishlistCount": 87,
    "forExchangeCount": 14,
    "forSaleCount": 6,
    "wantToBuyCount": 22
  }
}
```

#### Admin authentication

Admin endpoints require a JWT issued by the User Library Service: RS256, validated with `JWT_PUBLIC_KEY`, with claim `role` = `admin`. No token or an invalid/expired token → `401`; a valid token without the admin role → `403`. Issuer and audience are validated only when `JWT_ISSUER` / `JWT_AUDIENCE` are configured. Outside Development the service refuses to start without `JWT_PUBLIC_KEY`.

#### `POST /games/sync` — Request

```json
{ "rawgSlug": "elden-ring" }
```

A numeric RAWG id is also accepted. Fetches the full RAWG detail (including description) and creates or updates the game. Returns `200` with the game detail; `404` if RAWG has no such game; `502` if RAWG fails; `503` if `RAWG_API_KEY` is not configured.

#### `PUT /games/{id}` — Request

Full replacement of the editable fields; platforms and genres are replaced by the given sets.

```json
{
  "title": "Elden Ring",
  "description": "string",
  "coverImageUrl": "https://media.gamex.rs/...",
  "releaseDate": "2022-02-25",
  "metacritic": 96,
  "platformIds": ["uuid"],
  "genreIds": ["uuid"]
}
```

`title` is required (max 255); `metacritic` 0–100; `coverImageUrl` must be an absolute https URL. Unknown platform/genre ids → `400`. Returns `200` with the game detail. Manual edits to a RAWG-backed game are overwritten by the next RAWG sync or nightly refresh of that game.

### 3.2 Platforms

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/platforms` | None | List all platforms |

### 3.3 Genres

| Method | Path | Auth | Description |
|---|---|---|---|
| `GET` | `/genres` | None | List all genres |

### 3.4 Admin — Catalog Events

| Method | Path | Auth | Description |
|---|---|---|---|
| `POST` | `/admin/catalog-events/replay` | Admin | Republish `catalog.platform.upserted` for all platforms, then `catalog.game.upserted` for all games (or only `?gameIds=uuid1,uuid2`). Used for the initial fill of consumer replicas and for recovery after DLQ incidents. Runs as a background job; returns `202 Accepted` with `{ "jobId": "uuid" }` (`429` if 10 replays are already queued). |

---

## 4. Data Model

### `games`

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) PK | UUID |
| `rawg_id` | INT | Nullable; null for manually added games |
| `rawg_slug` | VARCHAR(200) | Nullable |
| `title` | VARCHAR(255) | Not null |
| `description` | TEXT | |
| `cover_image_url` | VARCHAR(500) | Cloudflare CDN URL pointing to S3 |
| `cover_source_url` | VARCHAR(500) | RAWG image URL the stored cover was copied from; internal, not returned by the API |
| `release_date` | DATE | |
| `metacritic` | SMALLINT | |
| `created_at` | DATETIME | |
| `updated_at` | DATETIME(6) | Bumped on any change to the game or its platforms; used as the version in `catalog.game.upserted` |

### `platforms`

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) PK | |
| `name` | VARCHAR(100) | e.g. `PlayStation 5` |
| `slug` | VARCHAR(100) | e.g. `playstation5` |
| `updated_at` | DATETIME(6) | Used as the version in `catalog.platform.upserted` |

### `genres`

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) PK | |
| `name` | VARCHAR(100) | |
| `slug` | VARCHAR(100) | |

### `game_platforms` (join)

| Column | Type |
|---|---|
| `game_id` | CHAR(36) FK |
| `platform_id` | CHAR(36) FK |

### `game_genres` (join)

| Column | Type |
|---|---|
| `game_id` | CHAR(36) FK |
| `genre_id` | CHAR(36) FK |

### `game_stats`

One row per game. Updated by the SQS consumer.

| Column | Type | Default |
|---|---|---|
| `game_id` | CHAR(36) PK FK → games.id | |
| `owned_count` | INT UNSIGNED | 0 |
| `wishlist_count` | INT UNSIGNED | 0 |
| `for_exchange_count` | INT UNSIGNED | 0 |
| `for_sale_count` | INT UNSIGNED | 0 |
| `want_to_buy_count` | INT UNSIGNED | 0 |
| `updated_at` | DATETIME | |

A row in `game_stats` is created automatically (via DB trigger or application logic) when a new row is inserted into `games`.

---

## 5. RAWG Integration

### Sync Strategy

1. **On search:** When a user searches for a game title in the frontend, the request hits `/games/search`. The service queries the local `games` table. If fewer than 3 results are found, it falls through to the RAWG API, persists returned games (matched by `rawg_id`), and responds from the newly cached data. RAWG search results carry no description, so games imported this way have none until an admin sync or the nightly refresh fetches the detail. If RAWG fails, times out (3s per attempt, 7s total) or no API key is configured, the local results are returned with `200`.
2. **On demand (Admin):** `POST /games/sync` accepts a RAWG slug and forces a full metadata refresh for that game.
3. **Nightly background job:** Refreshes metadata (title, description, metacritic, cover) for all games that have at least one active listing in the last 30 days. Runs at 03:00 local time via a .NET `IHostedService` cron.

### Image Storage

When a game is synced from RAWG and its RAWG image URL differs from `games.cover_source_url` (new game, or RAWG changed the cover), the cover is queued for a background worker so the sync itself stays fast:
1. Download cover image from RAWG CDN URL (max 10 MB)
2. Upload to S3: `s3://{S3_MEDIA_BUCKET}/{S3_MEDIA_PREFIX}game-covers/{rawg_id}.jpg`
3. Store `{CLOUDFLARE_MEDIA_BASE_URL}/{key}?v={hash}` in `games.cover_image_url` and the RAWG URL in `games.cover_source_url`. The `v` hash is derived from the RAWG URL, so the CDN serves the new image when the cover changes even though the S3 key stays the same.
4. Publish `catalog.game.upserted` (the cover is a replicated field)

A freshly imported game therefore has `coverImageUrl: null` for a moment. Cover images are never fetched from RAWG at request time — only in the background after a sync. A failed copy is logged and retried by the next sync of that game.

### Publishing after sync

Every sync path (search fallback, admin sync, admin `PUT /games/{id}`, nightly refresh) publishes a `catalog.game.upserted` event after the game is persisted — but only when replicated fields actually changed (title, cover, release date, metacritic, platforms) or the game is new. A newly discovered platform publishes `catalog.platform.upserted` **before** any game event that references it. See §6.

### RAWG API Key

Stored in AWS Secrets Manager as `gx/{env}/rawg-api-key`. Loaded at service startup. Locally provided via `.env`.

---

## 6. SQS Events

### 6.1 Published: `gamex-catalog-events`

Consumed by the User Library Service to maintain its read-only replica (`catalog_games`, `catalog_platforms`, `catalog_game_platforms`). Events carry the **full current state** of the replicated fields (not a diff), so consumers can upsert without reading anything else.

**Event: `catalog.platform.upserted`**

```json
{
  "eventType": "catalog.platform.upserted",
  "platformId": "uuid",
  "name": "PlayStation 5",
  "slug": "playstation5",
  "updatedAt": "2026-10-24T10:00:00.123456Z"
}
```

**Event: `catalog.game.upserted`**

```json
{
  "eventType": "catalog.game.upserted",
  "gameId": "uuid",
  "title": "Elden Ring",
  "coverImageUrl": "https://media.gamex.rs/game-covers/326243.jpg",
  "releaseDate": "2022-02-25",
  "metacritic": 96,
  "platformIds": ["uuid", "uuid"],
  "updatedAt": "2026-10-24T10:00:00.123456Z"
}
```

- `updatedAt` is `games.updated_at` / `platforms.updated_at` and acts as a version: consumers ignore events older than what they already stored.
- Only replicated fields are included. Description, genres and stats are **not** replicated — consumers that need them call the public API from the frontend.
- Delivery is at-least-once; duplicates are safe because consumers upsert by version.
- If publishing fails after the DB write, the game will be republished on its next change, by the nightly job, or by `POST /admin/catalog-events/replay`. (A transactional outbox can be added later if needed.)

### 6.2 Consumed: `gamex-library-events`

The service consumes the `gamex-library-events` queue published by the User Library Service.

### Consumed Event: `library.game.status.changed`

```json
{
  "eventType": "library.game.status.changed",
  "gameId": "uuid",
  "userId": "uuid",
  "delta": {
    "owned": 1,
    "wishlist": 0,
    "forExchange": 0,
    "forSale": 0,
    "wantToBuy": 0
  },
  "occurredAt": "2026-05-12T10:00:00Z"
}
```

`delta` values are `+1` (status added) or `-1` (status removed). Multiple statuses can change in one event (e.g., a game moved from `forExchange` to `forSale` sends `forExchange: -1, forSale: +1`).

A user can hold the same game on several platforms / formats (separate library entries). The User Library Service computes deltas on the per-user aggregate for the game, so all counts in `game_stats` are **number of users**, not number of copies — a user who owns a game on both PS5 and PC counts once in `owned_count`. A user can count in both `owned_count` and `wishlist_count` at the same time. No change to this consumer is needed.

### Consumer Behaviour

```sql
UPDATE game_stats
SET
  owned_count      = GREATEST(0, owned_count      + :delta_owned),
  wishlist_count   = GREATEST(0, wishlist_count   + :delta_wishlist),
  for_exchange_count = GREATEST(0, for_exchange_count + :delta_forExchange),
  for_sale_count   = GREATEST(0, for_sale_count   + :delta_forSale),
  want_to_buy_count = GREATEST(0, want_to_buy_count + :delta_wantToBuy),
  updated_at       = NOW()
WHERE game_id = :gameId;
```

`GREATEST(0, ...)` guards against counts going negative due to out-of-order or duplicate messages.

If the `game_id` does not exist locally (race condition during first sync), the event is requeued once; if it fails again it is sent to a Dead Letter Queue (`gamex-library-events-dlq`) for manual inspection.

---

## 7. Error Handling

All error responses follow RFC 7807 Problem Details:

```json
{
  "type": "https://gamex.rs/errors/not-found",
  "title": "Game not found",
  "status": 404,
  "detail": "No game with id abc-123 exists in the catalog"
}
```

---

## 8. Local Development

### Prerequisites
- Docker & Docker Compose
- .NET 10 SDK

### docker-compose.yml (within this repo)

```yaml
services:
  game-catalog-svc:
    build: .
    ports:
      - "5001:8080"
    environment:
      - DB_CONNECTION_STRING=Server=catalog-db;Database=games_db;User=root;Password=localdev;
      - RAWG_API_KEY=${RAWG_API_KEY}
      - SQS_LIBRARY_EVENTS_URL=http://localstack:4566/000000000000/gamex-library-events
      - SQS_CATALOG_EVENTS_URL=http://localstack:4566/000000000000/gamex-catalog-events
      - AWS_DEFAULT_REGION=eu-central-1
    depends_on:
      - catalog-db

  catalog-db:
    image: mysql:8.0
    environment:
      MYSQL_DATABASE: games_db
      MYSQL_ROOT_PASSWORD: localdev
    ports:
      - "3307:3306"
    volumes:
      - catalog-db-data:/var/lib/mysql

volumes:
  catalog-db-data:
```

Environment variables for local dev are provided via `.env` (not committed to git). For SQS locally, use [LocalStack](https://github.com/localstack/localstack) or configure the consumer to no-op when `ASPNETCORE_ENVIRONMENT=Development`.

### Run without Docker

```bash
cd src
dotnet run --project API
```

Set connection string and other env vars in `appsettings.Development.json` or via user secrets.

---

## 9. CI/CD

Defined in `.github/workflows/`:

| Workflow | Trigger | Action |
|---|---|---|
| `ci.yml` | Push to any branch, PR | Build + unit + integration tests |
| `deploy-dev.yml` | Merge to `main` | Build Docker image → push to ECR → update ECS service in `gx-dev` |
| `deploy-prod.yml` | Tag `v*.*.*` | Manual approval → push to ECR → update ECS service in `gx-prod` |

Docker image tag: `{ECR_REPO}/gamex-game-catalog-svc:{git-sha}`

---

## 10. Environment Variables Reference

| Variable | Description | Source (AWS) |
|---|---|---|
| `DB_CONNECTION_STRING` | MySQL connection string | Secrets Manager |
| `RAWG_API_KEY` | RAWG API key | Secrets Manager |
| `SQS_LIBRARY_EVENTS_URL` | SQS queue URL (consumed) | SSM Parameter Store |
| `SQS_CATALOG_EVENTS_URL` | SQS queue URL (published) | SSM Parameter Store |
| `S3_MEDIA_BUCKET` | S3 bucket name for cover images | SSM Parameter Store |
| `CLOUDFLARE_MEDIA_BASE_URL` | Cloudflare media base URL | SSM Parameter Store |
| `S3_MEDIA_PREFIX` | Key prefix per environment (`dev/`, `prod/`); the media bucket is shared | ECS task definition |
| `RAWG_BASE_URL` | Optional; defaults to `https://api.rawg.io/api/` | — |
| `AWS_SERVICE_URL` | Local only: LocalStack endpoint for S3/SQS | — |
| `DB_MIGRATE_ON_STARTUP` | Apply EF Core migrations at startup (default: true in Development) | ECS task definition |
| `JWT_PUBLIC_KEY` | RS256 public key for JWT validation (PEM; `\n` escapes allowed) | SSM Parameter Store |
| `JWT_ISSUER` | Optional; expected `iss` claim | SSM Parameter Store |
| `JWT_AUDIENCE` | Optional; expected `aud` claim | SSM Parameter Store |
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Production` | ECS task definition |
