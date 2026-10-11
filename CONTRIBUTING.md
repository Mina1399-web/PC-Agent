# Contributing to Host Agent

First-party contributions are accepted under the [MIT license](LICENSE).
Submit only work you have permission to license. Existing third-party code,
notices and license obligations retain their own terms; do not relabel them.

Open a pull request with a concrete behavior, relevant tests and actual
verification results. Security-sensitive changes require explicit review of
privilege, approval, identity, data and network boundaries. Never include
credentials, private keys, production configuration, databases or raw logs.
Report suspected vulnerabilities privately through the repository's GitHub
security advisory facility if enabled; otherwise request a private contact
from the owner without posting exploit details or sensitive data publicly.

Use the approved Windows DEV VM for ordinary development and tests. The owner
also explicitly authorized GitHub-hosted builds/tests for the SignPath path.
This exception does not allow using the production PC for development,
changing its security policies, or running destructive/live-secret tests.
Do not reinterpret existing VM guards as optional or remove them to get CI
green. A future cloud pipeline needs a separately tested, documented build
path and honestly identified test coverage.

Repository owner, maintainer and proposed signing approver:
[@Mina1399-web](https://github.com/Mina1399-web). This describes the proposed
roles, not verified GitHub or SignPath permission settings. AI assistance is
not an independent human review or signing approval.

See [Code signing policy](docs/operations/code-signing-policy.md) and
[SignPath preparation](docs/operations/signpath-preparation.md).
