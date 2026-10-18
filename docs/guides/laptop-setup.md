# Run QualGuard IQ on a Windows laptop

These steps put the whole application (web app, API, background worker, database, file storage and virus scanning) on one Windows computer with Docker Desktop. Everything runs in containers; nothing is installed into Windows itself.

The steps use `C:\QualGuard-IQ` as the folder. Every command is written with the full folder path, so you can run each one from any PowerShell window.

## What you need

- Docker Desktop for Windows, running (whale icon in the taskbar shows "Docker Desktop is running"). WSL 2 backend.
- Git for Windows.
- About 10 GB of free disk space and at least 8 GB of memory (16 GB is better).
- Access to the private GitHub repository `shawnparnell81/QualGuard-IQ`.
- For Cloudflare R2 file storage (optional): the R2 bucket `qms-files`, its S3 API address and an Account API token with Object Read & Write on that bucket (Access Key ID and Secret Access Key).

A PostgreSQL server already installed on the laptop (for example PostgreSQL 18 on port 5432) is not used and does not need to be stopped. The application's own database runs inside Docker and is not published on any Windows port, so the two never clash.

## First-time setup

1. Open **PowerShell** (Start menu, type PowerShell).

2. Download the code. Git opens a browser window the first time so you can sign in to GitHub.

   ```powershell
   git clone https://github.com/shawnparnell81/QualGuard-IQ.git C:\QualGuard-IQ
   ```

3. Create your settings file from the example.

   ```powershell
   Copy-Item C:\QualGuard-IQ\.env.example C:\QualGuard-IQ\.env
   ```

4. Open the settings file in Notepad.

   ```powershell
   notepad C:\QualGuard-IQ\.env
   ```

5. Fill in these values, then save and close Notepad. Use letters, numbers and `- _ . ! @` in passwords (no `$`, `;`, quotes or spaces).

   | Setting | What to enter |
   | --- | --- |
   | `POSTGRES_PASSWORD` | A new password for the application's database (you never type it anywhere else) |
   | `ADMIN_EMAIL` | Your email address. This becomes the first administrator's sign-in name |
   | `ADMIN_PASSWORD` | A temporary first password, at least 12 characters and not a common password |
   | `ADMIN_NAME` | Your name as it should appear in the app, for example `Shawn Parnell` |
   | `ORGANIZATION_NAME` | Your company name |
   | `MINIO_ROOT_PASSWORD` | A password of at least 8 characters for the built-in file storage (needed only when you keep MinIO, see step 6) |

   Leave `WEB_PORT=8081` and `APP_BASE_URL=http://localhost:8081` as they are unless port 8081 is already used on the laptop (see Troubleshooting).

6. Choose where files are stored.

   - **Built-in storage (MinIO, default):** keep `COMPOSE_PROFILES=minio` and leave every `STORAGE_` value empty except `STORAGE_BUCKET=qms-files` and `STORAGE_FORCE_PATH_STYLE=true`.
   - **Cloudflare R2:** change the lines to the following, using your own values from the R2 page, then save:

     ```
     COMPOSE_PROFILES=
     STORAGE_SERVICE_URL=https://<your-account-id>.r2.cloudflarestorage.com
     STORAGE_BUCKET=qms-files
     STORAGE_REGION=auto
     STORAGE_ACCESS_KEY=<Access Key ID>
     STORAGE_SECRET_KEY=<Secret Access Key>
     STORAGE_FORCE_PATH_STYLE=true
     ```

     `STORAGE_SERVICE_URL` is the **S3 API** address from the bucket's settings page without `/qms-files` on the end. With `COMPOSE_PROFILES` empty, MinIO is not started at all. The bucket must already exist; the app only checks it and never tries to create it.

7. Build and start everything with one command. The first run downloads and builds the images and takes about 10 to 20 minutes. Later starts take under a minute.

   ```powershell
   docker compose -f C:\QualGuard-IQ\docker-compose.yml up -d --build
   ```

8. Check that everything is up. Wait until `api` shows `healthy` (the virus scanner downloads its signatures on the first start, so this can take 3 to 5 minutes).

   ```powershell
   docker compose -f C:\QualGuard-IQ\docker-compose.yml ps
   ```

9. Open the app in your browser: **http://localhost:8081**

10. Sign in as the first administrator:
    - Email: the `ADMIN_EMAIL` from your `.env` file.
    - Password: the `ADMIN_PASSWORD` from your `.env` file.
    - The app then asks you to choose a new password straight away. The `.env` password works only for this first sign-in.

    The administrator account is created when the API starts and no account with `ADMIN_EMAIL` exists yet, so normally only on the first start. Changing `ADMIN_PASSWORD` in `.env` later does not change the existing account (use **Forgot password** or the Users page instead), and changing `ADMIN_EMAIL` would create a second administrator with that address, so leave it as it is.

## Everyday use

Docker Desktop must be running before these commands work.

Stop the app (your data is kept):

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml stop
```

Start it again:

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml start
```

The containers also start by themselves when Docker Desktop starts, unless you stopped them.

## Getting updates

1. Download the latest code.

   ```powershell
   git -C C:\QualGuard-IQ pull
   ```

2. Rebuild and restart. Database changes are applied automatically when the API starts; your data is kept.

   ```powershell
   docker compose -f C:\QualGuard-IQ\docker-compose.yml up -d --build
   ```

3. If `.env.example` gained new settings, compare it with your `.env` and copy any new lines across.

## Troubleshooting

See what is running and whether it is healthy:

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml ps
```

Read the API's messages (the most useful place to look when the app does not open):

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml logs --tail 200 api
```

| Problem | What to do |
| --- | --- |
| `required variable POSTGRES_PASSWORD is missing a value` (or `ADMIN_EMAIL`, `ADMIN_PASSWORD`) | The `.env` file is missing or a value is empty. Repeat steps 3 to 5 |
| `port is already allocated` for 8081 | Another program uses port 8081. Set `WEB_PORT=8091` and `APP_BASE_URL=http://localhost:8091` in `.env`, run step 7 again and open http://localhost:8091 |
| `api` stays `starting` or `unhealthy` for more than 10 minutes | Read the API log (above). On the first start the virus scanner may still be loading; wait and check `ps` again |
| API log says `Initial administrator could not be created` | `ADMIN_PASSWORD` breaks the password rules (at least 12 characters, not a common password). Change it in `.env`, then run step 7 again |
| API log mentions storage, `AccessDenied` or `InvalidAccessKeyId` | Check the `STORAGE_` values. For R2: the address has no bucket on the end, `STORAGE_REGION=auto`, and the token has Object Read & Write on `qms-files` |
| MinIO is `unhealthy` | `MINIO_ROOT_PASSWORD` must be at least 8 characters |
| Containers stop or the laptop slows down badly | Give Docker more memory: Docker Desktop → Settings → Resources (WSL 2 uses up to half the computer's memory by default) |
| Docker commands say Docker is not running | Start Docker Desktop and wait for "Engine running" |

### Optional: connect a database tool

The application's database is private to Docker by default. To look at it with pgAdmin or DBeaver, start it with the extra file below. It is published on `localhost:5433` (set `POSTGRES_HOST_PORT` in `.env` to change it), so it does not collide with a PostgreSQL server on 5432. Database `qgiq`, user `qgiq`, password `POSTGRES_PASSWORD` from `.env`.

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml -f C:\QualGuard-IQ\docker-compose.db-port.yml up -d
```

### Start over with an empty database

This deletes every record, uploaded file (MinIO only) and user in the local installation. It cannot be undone.

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml down -v
```

Then run step 7 again; the first administrator is created again from `.env`.
