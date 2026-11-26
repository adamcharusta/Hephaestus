# Debian-based test image for the injection spike: the stock image, which
# already has glibc and bash. EXTRA_PACKAGES adds what a finding shows is needed.
FROM debian:13-slim
ARG EXTRA_PACKAGES=""
RUN if [ -n "$EXTRA_PACKAGES" ]; then \
      apt-get update \
      && apt-get install --yes --no-install-recommends $EXTRA_PACKAGES \
      && rm -rf /var/lib/apt/lists/*; \
    fi
