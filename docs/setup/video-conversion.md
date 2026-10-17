# Video playback copies

Videos attached to records (.mp4, .m4v, .webm, .mov) are stored exactly as uploaded. When a converter is
configured, the worker also makes a **playback copy** that plays in every current browser:

* **MP4** — H.264 (High profile, yuv420p) with AAC audio and fast-start, always;
* **WebM** — VP9 with Opus audio, when `Video:Formats` includes `webm`.

The original is never changed and can always be downloaded. Players use the playback copy when it is ready and
the original otherwise.

## Configuration (worker and API hosts)

| Setting | Default | Meaning |
|---|---|---|
| `Video:FfmpegPath` | *(empty: no conversion)* | Full path to `ffmpeg`, e.g. `/usr/bin/ffmpeg`. Install the distribution package (`apt install ffmpeg`). |
| `Video:FfprobePath` | next to ffmpeg | Full path to `ffprobe`. |
| `Video:Formats` | `mp4` | `mp4` or `mp4,webm`. |
| `Video:MaxHeight` | `1080` | Larger videos are scaled down (aspect ratio kept). |
| `Video:TimeoutMinutes` | `30` | A conversion running longer is stopped and retried. |
| `Video:MaxAttempts` | `3` | Attempts before a conversion is marked Failed (retries after 5 min, 30 min, 2 h). |

Environment variables use double underscores, e.g. `Video__FfmpegPath=/usr/bin/ffmpeg`.

Without `Video:FfmpegPath` uploads are still accepted and queued; the content library shows *Playback copies are not
being made* with this requirement, nothing is marked as converted, and no attempts are used up. Queued videos are
converted once a converter is configured.

## How a conversion is accepted

1. The original is read from storage and its SHA-256 checked.
2. `ffprobe` reads the source (codec, duration, size). A file with no video stream fails.
3. `ffmpeg` runs as a separate process with an argument list (no shell), under the timeout.
4. The output is probed again: it must exist, use the expected codec (h264 or vp9) and last as long as the source
   (within 1 s or 2 %). Only then is it stored, hashed and marked **Ready**.
5. Failures are retried; after the last attempt the copy is **Failed** with the reason, and an editor of the record
   can choose **Retry conversion**.

An identical upload (same SHA-256) reuses the existing validated copy instead of converting again. Two workers never
convert the same copy (the job claims each item atomically) and a conversion interrupted by a worker restart is
requeued.

## Security

Playback copies follow the original's rules: viewing the record is required, Restricted files need edit rights,
quarantined originals are never converted or served, Confidential/Restricted opens are written to the security log,
and every copy is checksum-verified when served. Status changes are recorded in the audit trail.
