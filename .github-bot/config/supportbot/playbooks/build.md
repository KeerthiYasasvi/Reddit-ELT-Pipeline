# Build Issues Playbook

## Common Build Errors

### Missing Dependencies
- Check package manager lock files (package-lock.json, yarn.lock, etc.)
- Verify all dependencies are listed in package.json or equivalent
- Run clean install: `npm ci` or `yarn install --frozen-lockfile`

### Version Mismatches
- Confirm Node.js/runtime version matches requirements
- Check for peer dependency conflicts
- Review `.nvmrc` or `.tool-versions` for required versions

### Build Tool Issues
- Clear build cache: `npm run clean` or equivalent
- Remove and reinstall node_modules
- Check for outdated build tools

### Environment-Specific
- Verify environment variables are set correctly
- Check for OS-specific path separators
- Review file permissions on build scripts

## Diagnostic Steps
1. Capture full build output with verbose logging
2. Identify the first error in the build log
3. Check for recent changes in dependencies
4. Verify build works in clean environment (Docker/CI)

## Safe Next Steps
- Run build with verbose logging: `npm run build --verbose`
- Check for breaking changes in recent dependency updates
- Try building with a clean environment
