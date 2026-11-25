# Alpine-based test image for the injection spike: the stock image (musl) plus
# bash, which the Project image contract requires and Alpine does not ship.
# EXTRA_PACKAGES adds what a finding shows is needed.
FROM alpine:3.23
ARG EXTRA_PACKAGES=""
RUN apk add --no-cache bash $EXTRA_PACKAGES
