# Runtime Issues Playbook

## Common Runtime Errors

### Null Reference / Undefined
- Check for missing initialization
- Verify object exists before accessing properties
- Review async/await patterns for race conditions

### Memory Issues
- Monitor memory usage over time
- Check for memory leaks (event listeners, closures)
- Review large data structure handling

### Performance Problems
- Profile code to identify bottlenecks
- Check for N+1 queries in database access
- Review algorithm complexity

### Crashes / Exceptions
- Capture full stack trace
- Check error logs for patterns
- Review recent code changes
- Verify input validation

## Diagnostic Steps
1. Reproduce issue with minimal test case
2. Enable debug logging
3. Add strategic console.log or breakpoints
4. Check for error patterns in logs
5. Test with different input data

## Safe Next Steps
- Add error handling around suspected code
- Implement input validation
- Add logging to trace execution flow
- Test with edge cases and boundary conditions
