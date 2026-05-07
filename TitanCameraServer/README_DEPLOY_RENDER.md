# TitanCameraServer Deploy on Render (One Click)

This guide deploys `TitanCameraServer` as a public WebSocket signaling service for Titan WebCam using `render.yaml`.

## 1) Prerequisites

- GitHub repo containing this project
- Render account
- Public frontend domain (for example Vercel)

## 2) One-Click with `render.yaml`

1. Push your latest source to GitHub.
2. Open Render dashboard.
3. Click **New** -> **Blueprint**.
4. Connect your GitHub repository.
5. Render auto-detects `TitanCameraServer/render.yaml`.
6. Click **Apply** to deploy one click.

`render.yaml` already defines:

- service name: `titan-camera-server`
- runtime: Docker
- autoDeploy: `true`
- health check path: `/health`
- env vars:
  - `PORT`
  - `ASPNETCORE_ENVIRONMENT=Production`
  - `CORS_ORIGINS`

## 3) Verify Deployment

After deploy, open:

- `https://<your-render-domain>/health`

Expected response:

```json
{
  "status": "ok",
  "service": "TitanCameraServer"
}
```

Example public URL:

- `https://titan-camera-server.onrender.com`

## 4) Compatibility Checklist

- Docker build: supported via `TitanCameraServer/Dockerfile`
- Render compatible: yes (`render.yaml` + Docker runtime)
- No localhost hardcode for production signaling flow
- WebSocket over WSS supported through Render HTTPS domain

## 5) Update Clients

- TitanWebCam default signaling URL should point to Render URL.
- Titan AI Live PC `RemoteSignalingServerUrl` should point to Render URL.

## 6) Production Notes

- Render URL provides HTTPS/WSS ready for 4G/5G/global usage.
- Keep tokens short-lived and do not log full token values.
- TURN server is strongly recommended for unstable remote networks.
