# The hephaestus-tools image: git and nothing else (AR-34, AR-38, Story 3.5). It is the image of the checkout container of every step and of the checkpoint container (Story 3.6),
# which run with no network and no credential and never run the Project's image. Build it from the repository root:  docker build -f deploy/tools.Dockerfile -t hephaestus-tools deploy
# The build stops when the git that was installed is older than 2.48, the version the architecture requires of both images (AR-3), so that the gateway and the checkout run one git behaviour on both sides of a bundle (ADR-0010).
FROM alpine:3.23
RUN apk add --no-cache git \
 && git --version | awk '{ split($3, v, "."); exit !(v[1] > 2 || (v[1] == 2 && v[2] >= 48)) }'
