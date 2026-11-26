# Test image for the BMAD spike (Story 1.2): the stock Debian image, which
# already has glibc and bash, plus what FR-63 says the execution image of a BMAD
# Project must provide: uv and Python. Nothing else.
#
# EXTRA_PACKAGES adds what a finding shows is needed. The spike builds the image
# twice: as it is, and with EXTRA_PACKAGES=git.
#
# uv comes from the vendor's own image on Docker Hub, pinned by version; its
# digest is recorded in results/01-prepare/images.txt.
FROM debian:13-slim
ARG EXTRA_PACKAGES=""
RUN apt-get update \
    && apt-get install --yes --no-install-recommends python3 $EXTRA_PACKAGES \
    && rm -rf /var/lib/apt/lists/*
COPY --from=astral/uv:0.13.9 /uv /usr/local/bin/uv
# No cell depends on this line: every container is started with
# --user 10001:10001. It makes the same step user the image's own default, so
# that nothing runs as root when the option is left out. Added after the runs.
USER 10001:10001
