# GitHub Security Advisory Access Guide

This guide helps you access and manage GitHub Security Advisories when facing access issues (e.g., antivirus blocking).

## Issue: Cannot Access GitHub Security Advisory Page

If your antivirus or security software is blocking access to GitHub's Security Advisory page, here are solutions:

### Solution 1: Browser-Based Workarounds

#### Using Different Browsers
1. Try accessing GitHub in:
   - Firefox (Private Window)
   - Chrome (Incognito Mode)
   - Edge (InPrivate Window)
   - Safari (Private Browsing)

#### Clearing Browser Data
```bash
# Clear all cache, cookies, and site data for github.com
# Then restart your browser and try again
```

#### Disabling Browser Extensions
1. Open browser in safe/guest mode (no extensions)
2. Navigate to the Security Advisory
3. Complete the "Accept and open as draft" action
4. Re-enable extensions after completion

### Solution 2: Antivirus Configuration

#### Temporary Exclusion
1. Add `github.com` to your antivirus exclusion list temporarily
2. Access the Security Advisory page
3. Complete the required action
4. Remove the exclusion after completion

**Note**: Only do this if you trust the source (GitHub is legitimate)

#### Whitelist Specific URLs
Add these URLs to your antivirus whitelist:
- `https://github.com/*/security/advisories/*`
- `https://github.com/Alex4SSB/ADB-Explorer/security/*`

### Solution 3: Alternative Devices/Networks

#### Mobile Device
1. Use GitHub mobile app or mobile browser
2. Navigate to: Repository → Security → Advisories
3. Accept and open the advisory as draft

#### Different Network
- Try accessing from a different network (mobile hotspot, different WiFi)
- Use a VPN if corporate firewall is blocking
- Try from a different device (work computer, tablet, etc.)

### Solution 4: Command-Line/API Access

Unfortunately, GitHub's API doesn't currently support accepting security advisories programmatically. The action must be performed through the web interface.

### Solution 5: GitHub Support

If all else fails:
1. Contact GitHub Support: https://support.github.com/
2. Explain the antivirus blocking issue
3. Request assistance in accepting the pending advisory

## Step-by-Step: Accepting a Security Advisory

Once you can access the page:

### 1. Navigate to Security Advisory
```
https://github.com/Alex4SSB/ADB-Explorer/security/advisories
```

### 2. Find the Pending Advisory
- Look for advisories in "Proposed" status
- Click on the advisory from @blankshiro

### 3. Review the Vulnerability Details
- Read the vulnerability description
- Review severity and impact
- Check any proof-of-concept code

### 4. Accept and Open as Draft
- Click "Accept and open as draft" button
- This converts it to a draft security advisory
- You maintain control over when it's published

### 5. Add Collaborator (Optional)
- You can add @blankshiro as a collaborator on the advisory
- This allows them to help edit and finalize details

### 6. Document the Fix
- Link to the commit that fixes the vulnerability
- Add any relevant patches or workarounds
- Update the affected versions

### 7. Request CVE (Automatic)
- GitHub will automatically request a CVE ID
- This happens when you publish the advisory
- No manual CVE request needed

### 8. Publish When Ready
- After fix is deployed in a release
- Click "Publish advisory"
- CVE is assigned automatically
- Advisory becomes public

## Alternative: Manual CVE Process

If you absolutely cannot access GitHub's advisory system:

### 1. Implement the Fix
- Commit and push your security fix
- Tag a new release with the fix
- Document the fix in release notes

### 2. Coordinate with Reporter
- Contact @blankshiro
- Provide them with:
  - Your fix commit SHA
  - Version number with the fix
  - Brief description of what was fixed

### 3. MITRE CVE Request
The reporter (@blankshiro) can submit to MITRE directly:
- URL: https://cveform.mitre.org/
- Include:
  - Product name: ADB Explorer
  - Vendor: Alex4SSB
  - Affected versions
  - Fixed version
  - Description of vulnerability
  - Fix commit URL

### 4. Credit the Reporter
- Mention @blankshiro in release notes
- Add to SECURITY.md Hall of Fame
- Link to CVE once assigned

## For the Current Situation (Issue #294)

Based on the comments in issue #294:

### What Happened
1. ✅ @blankshiro reported vulnerability privately
2. ✅ You enabled private vulnerability reporting
3. ✅ @blankshiro submitted the full report
4. ✅ You reviewed it (via email)
5. ✅ You committed a fix
6. ❌ Cannot access advisory page to accept (AV blocking)
7. ⏳ Pending: Accept and publish advisory

### What to Do
**Option A - Try to Access** (Recommended)
1. Try solutions above to access the advisory
2. Accept and open as draft
3. Link your fix commit
4. Publish the advisory
5. GitHub assigns CVE automatically

**Option B - Manual Process** (If Option A fails)
1. Confirm with @blankshiro they'll submit to MITRE
2. Provide them details of your fix:
   - Commit SHA with the fix
   - Version where it's fixed
   - Brief description
3. They submit to MITRE directly
4. You credit them in release notes

## URLs Quick Reference

- **Security Advisories**: `https://github.com/[owner]/[repo]/security/advisories`
- **Security Overview**: `https://github.com/[owner]/[repo]/security`
- **MITRE CVE Form**: `https://cveform.mitre.org/`
- **GitHub Support**: `https://support.github.com/`

## Additional Resources

- [GitHub Security Advisories Documentation](https://docs.github.com/en/code-security/security-advisories)
- [Coordinated Disclosure of Security Vulnerabilities](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing/privately-reporting-a-security-vulnerability)
- [CVE Numbering Authority](https://www.cve.org/)
