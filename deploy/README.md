# deploy

Deployment files of the spine's source tree. Each is added by the story that first needs it:

| File | Story |
| --- | --- |
| `compose.yaml`, `Dockerfile` | 1.4 |
| `agents.lock` | 3.1 |
| `tools.Dockerfile` | the story that first starts a helper container |
