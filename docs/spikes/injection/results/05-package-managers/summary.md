| Manager | Image | Generic variables alone | JAVA_TOOL_OPTIONS | settings.xml proxy | Hosts asked of the proxy |
| --- | --- | --- | --- | --- | --- |
| npm | node@sha256:873f586f3794f52281bd039bf02c048a0be08fde619338f9941bad1eb6b34530 | works | not tried | not tried | registry.npmjs.org:443 |
| dotnet | mcr.microsoft.com/dotnet/sdk@sha256:8c6edfb26c85153f236d26efebd9ad349aec403583f343f201ee67838b23658d | works | not tried | not tried | api.nuget.org:443 crl3.digicert.com |
| pip | python@sha256:22eafb370920ad8b429e0aec3fff433e0669968519f5d60469d83729c138b28b | works | not tried | not tried | files.pythonhosted.org:443 pypi.org:443 |
| cargo | rust@sha256:3b199fd6db7d709274df56565b8f4003f9e30d7bfcff9d53148c86053b7855e2 | works | not tried | not tried | index.crates.io:443 static.crates.io:443 |
| go | golang@sha256:699337d620559a59b4a2bb298ad59611e535d2ee755a34cf2d2a98f37578dc80 | works | not tried | not tried | proxy.golang.org:443 sum.golang.org:443 |
| maven | maven@sha256:af3a049b2808ffaeccd31e2c2eba0d47776468d1e4f2fd6c1f9fd2f89aabf602 | fails | fails | works | repo.maven.apache.org:443 |
| gradle | gradle@sha256:39bf36650445bcef23fe6a4ceaf66f60569d1bad0b91e2049405faa6d841d2ee | fails | works | not tried | repo.maven.apache.org:443 |
