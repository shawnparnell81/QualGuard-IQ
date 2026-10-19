# Run QualGuard IQ on a Windows laptop

These steps put the whole application (web app, API, background worker, database and file storage) on one Windows computer with Docker Desktop. Virus scanning of uploads is optional (see step 6b). Everything runs in containers; nothing is installed into Windows itself.

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
   | `POSTGRES_PASSWORD` | A new password for the database owner, used only to update the database (you never type it anywhere else) |
   | `POSTGRES_APP_PASSWORD` | A second, different password (at least 12 characters) for the limited login the app uses every day |
   | `ADMIN_EMAIL` | Your email address. This becomes the first administrator's sign-in name |
   | `ADMIN_PASSWORD` | A temporary first password, at least 12 characters and not a common password |
   | `ADMIN_NAME` | Your name as it should appear in the app, for example `Shawn Parnell` |
   | `ORGANIZATION_NAME` | Your company name |
   | `MINIO_ROOT_PASSWORD` | A password of at least 8 characters for the built-in file storage (needed only when you keep MinIO, see step 6) |
   | `MINIO_APP_PASSWORD` | A different password of at least 8 characters for the app's own storage login, which can only reach the file bucket (MinIO only) |

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

6b. Virus scanning (optional). It is off by default: uploads are stored and marked "Not scanned", and administrators see a notice on the Users and Organization pages. To turn it on, set `MALWARE_SCANNER=ClamAV` and add `clamav` to the profiles, for example `COMPOSE_PROFILES=minio,clamav` (or `COMPOSE_PROFILES=clamav` with R2). It works on Intel, AMD and ARM laptops (for example Snapdragon) and needs about 3 GB of extra memory. If the scanner stops answering, administrators see a red notice and uploads are refused until it is back.

7. Build and start everything with one command. The first run downloads and builds the images and takes about 10 to 20 minutes. Later starts take under a minute.

   ```powershell
   docker compose -f C:\QualGuard-IQ\docker-compose.yml up -d --build
   ```

8. Check that everything is up. Wait until `api` shows `healthy`. `migrate` (and `minio-init` with MinIO) run once and then show `exited (0)`; that is expected. With virus scanning on, the first start takes 3 to 5 minutes longer while it downloads its signatures.

   ```powershell
   docker compose -f C:\QualGuard-IQ\docker-compose.yml ps
   ```

9. Open the app in your browser: **http://localhost:8081**

10. Sign in as the first administrator:
    - Email: the `ADMIN_EMAIL` from your `.env` file.
    - Password: the `ADMIN_PASSWORD` from your `.env` file.
    - The app then asks you to choose a new password straight away. The `.env` password works only for this first sign-in.
    - On a new installation the app also asks you to set up an authenticator app (for example Microsoft Authenticator or Google Authenticator on your phone), because administrators need two-step verification. Keep the recovery codes it shows somewhere safe. To skip this, set `ADMIN_REQUIRES_MFA=false` in `.env` before the first start; you can change it later in Admin › Roles.

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

2. If `.env.example` gained new settings, compare it with your `.env` and copy any new lines across, filling in the values (for example `POSTGRES_APP_PASSWORD` and `MINIO_APP_PASSWORD`).

3. Rebuild and restart. The `migrate` step applies database changes before the API starts; your data is kept.

   ```powershell
   docker compose -f C:\QualGuard-IQ\docker-compose.yml up -d --build
   ```

### Updating an installation from before the separate `migrate` step

Installations set up before the security update (October 2026) need two new lines in `.env` before step 3. Without them, `docker compose` stops with `required variable POSTGRES_APP_PASSWORD is missing a value`, or `minio-init` exits with an error.

```
POSTGRES_APP_PASSWORD=<a new password, at least 12 characters, different from POSTGRES_PASSWORD>
MINIO_APP_PASSWORD=<a new password, at least 8 characters, different from MINIO_ROOT_PASSWORD>
```

Leave every other line as it is, including `POSTGRES_PASSWORD`, `MINIO_ROOT_PASSWORD`, `ADMIN_EMAIL` and `ADMIN_PASSWORD`. Skip `MINIO_APP_PASSWORD` if you use Cloudflare R2 instead of MinIO. You choose both new passwords yourself; nothing else needs them.

What happens on the first start after the update:

- The existing database, files and key ring are kept. `migrate` signs in with the existing owner login (`POSTGRES_PASSWORD`), applies the update and creates the limited `qgiq_app` login inside the same database. `minio-init` creates the bucket-only `qgiq-app` storage login next to the existing files.
- Everyone, including you, is signed out once and signs in again with their usual password.
- Your administrator account is not switched to two-step verification. `ADMIN_REQUIRES_MFA` only applies to new installations; to require it, turn on **Requires two-step verification** for the Administrator role in Admin › Roles.

## Backing up

Make a backup before every update and regularly, for example once a week. Docker Desktop and the app must be running. This command saves the database and the key ring (the keys that unlock two-step verification and saved integration passwords) into a new dated folder in `C:\QualGuard-IQ\backups`:

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml run --rm backup
```

It finishes with `Backup written to /backups/qgiq-<date and time>`. To see your backups:

```powershell
Get-ChildItem C:\QualGuard-IQ\backups
```

- Copy each new folder somewhere off the laptop, for example OneDrive or a USB drive. A backup that only lives on the laptop is lost with the laptop.
- Keep the folders private. `keys.tar.gz` unlocks your two-step verification and saved passwords, so treat it like a password.
- Uploaded files are not in this backup. With Cloudflare R2 they stay safe in R2. With MinIO they are in Docker's `miniodata` volume; see the recovery guide (`docs/guides/recovery-guide.md`) for copying them.
- To restore, follow the recovery guide.

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
| `required variable POSTGRES_PASSWORD is missing a value` (or `POSTGRES_APP_PASSWORD`, `ADMIN_EMAIL`, `ADMIN_PASSWORD`) | The `.env` file is missing or a value is empty. Repeat steps 3 to 5 (after an update, copy the new lines from `.env.example`) |
| `migrate` exited with an error | Read its log: `docker compose -f C:\QualGuard-IQ\docker-compose.yml logs migrate`. A message about `AppRolePassword` means `POSTGRES_APP_PASSWORD` is shorter than 12 characters or the same as `POSTGRES_PASSWORD` |
| `minio-init` exited with an error | `MINIO_APP_PASSWORD` is missing, shorter than 8 characters, or the same as `MINIO_ROOT_PASSWORD` |
| `port is already allocated` for 8081 | Another program uses port 8081. Set `WEB_PORT=8091` and `APP_BASE_URL=http://localhost:8091` in `.env`, run step 7 again and open http://localhost:8091 |
| `api` stays `starting` or `unhealthy` for more than 10 minutes | Read the API log (above). With virus scanning on, the scanner may still be loading on the first start; wait and check `ps` again |
| API log says `Initial administrator could not be created` | `ADMIN_PASSWORD` breaks the password rules (at least 12 characters, not a common password). Change it in `.env`, then run step 7 again |
| API log mentions storage, `AccessDenied` or `InvalidAccessKeyId` | Check the `STORAGE_` values. For R2: the address has no bucket on the end, `STORAGE_REGION=auto`, and the token has Object Read & Write on `qms-files` |
| MinIO is `unhealthy` | `MINIO_ROOT_PASSWORD` must be at least 8 characters |
| Containers stop or the laptop slows down badly | Give Docker more memory: Docker Desktop → Settings → Resources (WSL 2 uses up to half the computer's memory by default) |
| Docker commands say Docker is not running | Start Docker Desktop and wait for "Engine running" |

### Optional: connect a database tool

The application's database is private to Docker by default. To look at it with pgAdmin or DBeaver, start it with the extra file below. It is published on `localhost:5433` (set `POSTGRES_HOST_PORT` in `.env` to change it), so it does not collide with a PostgreSQL server on 5432. Database `qgiq`, user `qgiq`, password `POSTGRES_PASSWORD` from `.env` (or the read-and-write login `qgiq_app` with `POSTGRES_APP_PASSWORD`).

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml -f C:\QualGuard-IQ\docker-compose.db-port.yml up -d
```

### Start over with an empty database

This deletes every record, uploaded file (MinIO only) and user in the local installation. It cannot be undone.

```powershell
docker compose -f C:\QualGuard-IQ\docker-compose.yml down -v
```

Then run step 7 again; the first administrator is created again from `.env`.
