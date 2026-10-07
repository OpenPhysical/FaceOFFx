# GitHub Actions workflows

## CI (`ci.yml`)

Pushes to master/develop and pull requests to master build and test on Ubuntu,
Windows and macOS across .NET 8, 9 and 10. A separate job verifies the bundled
NuGet package, package consumers and CLI JSON output.

## Trusted publishing (`publish.yml`)

Manual dispatch builds, tests, packs and smoke-tests one FaceOFFx package.
Publication requires `publish=true` and an exact `v<Version>` tag.
NuGet/login exchanges GitHub OIDC for a short-lived key immediately before push.
No long-lived NuGet API key is used.

NuGet policy settings:

- Package owner and login user: `mistial-dev`
- Repository owner: `mistial-dev`
- Repository: `FaceOFFx`
- Workflow: `publish.yml`
- Environment: `nuget-production`
- Scope: push only new package versions
- Package pattern: `FaceOFFx` (no wildcard)

Configure the GitHub `nuget-production` environment to allow version tags only.
Create and push a signed version tag, then dispatch:

```sh
gh workflow run publish.yml --ref v4.0.0 -f publish=true
```

A dispatch without publication validates artifacts without logging into NuGet.

## GitHub release (`release.yml`)

Version-tag pushes create a release with the verified library package and merged
CycloneDX/SPDX inventories. NuGet publication is dispatched separately after
release validation. Older NuGet versions can be unlisted through Manage Packages;
unlisting preserves existing consumers that request an exact version.
