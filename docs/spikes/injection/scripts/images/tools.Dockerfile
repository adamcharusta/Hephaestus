# Helper image for the injection spike: git only, like the hephaestus-tools
# image the architecture names for checkout and checkpoint containers. It
# prepares the test repository; no Agent ever runs in it.
FROM alpine:3.23
RUN apk add --no-cache git
