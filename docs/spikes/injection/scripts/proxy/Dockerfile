# Egress proxy for the injection spike. It stands in for the product's egress
# proxy, which is not chosen yet: any forward HTTP proxy that supports CONNECT
# serves the purpose. tinyproxy was picked because it is small, free and
# open-source (GPL-2.0), and logs every CONNECT host.
#
# The container runs unprivileged: non-root from the start, no volume, no
# credential, no Docker socket.
FROM alpine:3.23
RUN apk add --no-cache tinyproxy
COPY tinyproxy.conf /etc/tinyproxy/tinyproxy.conf
USER tinyproxy
EXPOSE 3128
ENTRYPOINT ["tinyproxy", "-d", "-c", "/etc/tinyproxy/tinyproxy.conf"]
