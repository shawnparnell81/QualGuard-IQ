#!/bin/sh
# One-time set-up for the bundled MinIO, run by docker compose on every start (safe to repeat):
# creates the file bucket and a login that can use only that bucket, so the app never holds the MinIO root keys.
set -eu
: "${MINIO_ROOT_USER:?}" "${MINIO_ROOT_PASSWORD:?}" "${APP_ACCESS_KEY:?}" "${APP_SECRET_KEY:?}" "${BUCKET:?}"
MINIO_URL=${MINIO_URL:-http://minio:9000}
MC=${MC:-mc}
export MC_CONFIG_DIR=${MC_CONFIG_DIR:-/tmp/.mc}
if [ "$APP_ACCESS_KEY" = "$MINIO_ROOT_USER" ]; then
  echo "The app storage key must differ from MINIO_ROOT_USER." >&2
  exit 1
fi
"$MC" alias set qgiq "$MINIO_URL" "$MINIO_ROOT_USER" "$MINIO_ROOT_PASSWORD" >/dev/null
"$MC" mb --ignore-existing "qgiq/$BUCKET" >/dev/null
POLICY=$(mktemp)
cat > "$POLICY" <<JSON
{"Version":"2012-10-17","Statement":[
 {"Effect":"Allow","Action":["s3:ListBucket","s3:GetBucketLocation","s3:ListBucketMultipartUploads"],"Resource":["arn:aws:s3:::$BUCKET"]},
 {"Effect":"Allow","Action":["s3:GetObject","s3:PutObject","s3:DeleteObject","s3:AbortMultipartUpload","s3:ListMultipartUploadParts"],"Resource":["arn:aws:s3:::$BUCKET/*"]}
]}
JSON
"$MC" admin policy create qgiq qgiq-app-files "$POLICY" >/dev/null
rm -f "$POLICY"
"$MC" admin user add qgiq "$APP_ACCESS_KEY" "$APP_SECRET_KEY" >/dev/null
"$MC" admin policy attach qgiq qgiq-app-files --user "$APP_ACCESS_KEY" >/dev/null 2>&1 || true
"$MC" admin user info qgiq "$APP_ACCESS_KEY" | grep -q qgiq-app-files
echo "MinIO bucket $BUCKET and app login $APP_ACCESS_KEY are ready."
