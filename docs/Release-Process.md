# Release Process Guide

This guide explains how to create releases for the AGI PDM Migrator.

## Automated Release Process

Releases are automatically created by GitHub Actions when you push a version tag. This is the recommended approach.

### Creating a Release

1. **Ensure all changes are committed and pushed to main**

2. **Create and push a version tag**:
   ```bash
   git tag -a v1.0.3 -m "Release v1.0.3

   Brief description of changes

   Features:
   - Feature 1
   - Feature 2

   Bug Fixes:
   - Fix 1
   - Fix 2"
   
   git push origin v1.0.3
   ```

3. **GitHub Actions will automatically**:
   - Build the application on Windows
   - Create release packages (zip and standalone exe)
   - Generate a GitHub release with your tag message
   - Upload the artifacts

### Version Numbering

Use semantic versioning (SemVer):
- MAJOR.MINOR.PATCH (e.g., 1.0.2)
- MAJOR: Breaking changes
- MINOR: New features (backwards compatible)  
- PATCH: Bug fixes and minor improvements

### Release Artifacts

Each release includes:
- `AGI-PDM-Migrator-vX.X.X.zip` - Complete package with executable and sample config
- `AGI-PDM-vX.X.X.exe` - Standalone executable only

## Manual Release Process (If Needed)

### Building Locally

```powershell
cd AGI-PDM
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

### Using GitHub CLI

```bash
# Create release with GitHub CLI
gh release create v1.0.3 \
  --title "AGI PDM Migrator v1.0.3" \
  --generate-notes \
  AGI-PDM-Migrator-v1.0.3.zip \
  AGI-PDM-v1.0.3.exe
```

## Release Checklist

Before creating a release:
- [ ] All tests pass
- [ ] Documentation is updated
- [ ] Version number follows SemVer
- [ ] Changes are documented in tag message
- [ ] Code builds without errors

## Troubleshooting

### Release Workflow Fails

1. Check GitHub Actions logs for specific errors
2. Ensure the workflow has `contents: write` permission
3. Verify the tag format matches `v*` pattern

### Missing Artifacts

The workflow packages only:
- The compiled executable
- A sample configuration file
- A minimal README

Source code is not included in release packages.