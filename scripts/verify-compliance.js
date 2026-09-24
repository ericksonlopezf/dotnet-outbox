// Copyright © Erickson Lopez. MIT License.
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
let failureCount = 0;

function logPass(msg) {
    console.log(`  \x1b[32m✔ PASS\x1b[0m: ${msg}`);
}

function logFail(msg) {
    console.error(`  \x1b[31m✖ FAIL\x1b[0m: ${msg}`);
    failureCount++;
}

function walk(dir, excludeDirs = ['bin', 'obj', '.git', 'StrykerOutput', 'BenchmarkDotNet.Artifacts', 'TestResults', 'coveragereport', 'node_modules', 'MEGA-AUDITORIA', 'audit-reports']) {
    let results = [];
    if (!fs.existsSync(dir)) return results;
    const list = fs.readdirSync(dir);
    for (const file of list) {
        if (excludeDirs.includes(file) || file.startsWith('coveragereport_') || file.startsWith('TestResults_')) continue;
        const filePath = path.join(dir, file);
        const stat = fs.statSync(filePath);
        if (stat.isDirectory()) {
            results = results.concat(walk(filePath, excludeDirs));
        } else {
            results.push(filePath);
        }
    }
    return results;
}

console.log('\n=============================================================');
console.log('  \x1b[1m🛡️ ERICKSONLOPEZ REPOSITORY COMPLIANCE & ENFORCEMENT\x1b[0m');
console.log('=============================================================\n');

// 1. Document Naming Audit (Kebab-Case)
console.log('\x1b[1m[1/7] Document Naming (Kebab-Case) Validation\x1b[0m');
const standardExceptions = new Set([
    'README.MD',
    'LICENSE',
    'SECURITY.MD',
    'CODE_OF_CONDUCT.MD',
    'CONTRIBUTING.MD',
    'CHANGELOG.MD',
    'GOVERNANCE.MD',
    'SUPPORT.MD',
    'ROADMAP.MD',
    'BOUNDARY.MD',
    'PULL_REQUEST_TEMPLATE.MD',
    'CODEOWNERS',
    'BUG_REPORT.MD',
    'FEATURE_REQUEST.MD',
    'ANALYZERRELEASES.SHIPPED.MD',
    'ANALYZERRELEASES.UNSHIPPED.MD'
]);

const mdFiles = walk(ROOT).filter(f => f.endsWith('.md'));
let namingViolations = 0;
for (const file of mdFiles) {
    const baseName = path.basename(file);
    const upper = baseName.toUpperCase();
    if (standardExceptions.has(upper)) {
        continue;
    }
    // Check if filename is kebab-case (lowercase letters, numbers, hyphens, dots)
    const isKebab = /^[a-z0-9]+(?:-[a-z0-9]+)*(?:\.[a-z0-9]+)*\.md$/.test(baseName);
    if (!isKebab) {
        logFail(`Non-kebab-case document detected: ${path.relative(ROOT, file)}`);
        namingViolations++;
    }
}
if (namingViolations === 0) {
    logPass(`All ${mdFiles.length} documentation and markdown files adhere to naming conventions.`);
}

// 2. Copyright Headers Audit
console.log('\n\x1b[1m[2/7] Copyright & MIT License Headers Validation\x1b[0m');
const CS_HEADER = '// Copyright © Erickson Lopez. MIT License.';
const XML_HEADER = '<!-- Copyright © Erickson Lopez. MIT License. -->';

const csFiles = walk(ROOT).filter(f => f.endsWith('.cs') && !f.endsWith('.g.cs') && !f.endsWith('.AssemblyInfo.cs') && !f.includes('Scratch'));
const xmlFiles = walk(ROOT).filter(f => (f.endsWith('.csproj') || f.endsWith('.props') || f.endsWith('.targets')) && !f.includes('Scratch'));

let headerViolations = 0;
for (const file of csFiles) {
    const firstLine = fs.readFileSync(file, 'utf8').split('\n')[0].trim();
    if (firstLine !== CS_HEADER) {
        logFail(`Missing/invalid C# header in: ${path.relative(ROOT, file)}`);
        headerViolations++;
    }
}
for (const file of xmlFiles) {
    const firstLine = fs.readFileSync(file, 'utf8').split('\n')[0].trim();
    if (firstLine !== XML_HEADER) {
        logFail(`Missing/invalid XML header in: ${path.relative(ROOT, file)}`);
        headerViolations++;
    }
}
if (headerViolations === 0) {
    logPass(`All ${csFiles.length} C# files and ${xmlFiles.length} XML/csproj/props files have valid Copyright headers.`);
}

// 3. No Obsolete APIs Audit (Zero Tolerance)
console.log('\n\x1b[1m[3/7] Obsolete APIs (Zero Tolerance) Validation\x1b[0m');
let obsoleteViolations = 0;
const srcCsFiles = walk(path.join(ROOT, 'src')).filter(f => f.endsWith('.cs'));
for (const file of srcCsFiles) {
    const content = fs.readFileSync(file, 'utf8');
    if (/\[\s*Obsolete\b/i.test(content)) {
        logFail(`[Obsolete] attribute detected in production code: ${path.relative(ROOT, file)}`);
        obsoleteViolations++;
    }
}
if (obsoleteViolations === 0) {
    logPass('Zero [Obsolete] attributes detected across all production source projects.');
}

// 4. CS1591 Suppression Audit (Zero Tolerance in Production)
console.log('\n\x1b[1m[4/7] CS1591 (XML Documentation) Suppression Audit\x1b[0m');
let cs1591Violations = 0;
const rootPropsPath = path.join(ROOT, 'Directory.Build.props');
if (fs.existsSync(rootPropsPath)) {
    const rootPropsContent = fs.readFileSync(rootPropsPath, 'utf8');
    if (rootPropsContent.includes('CS1591') || rootPropsContent.includes('1591')) {
        logFail(`CS1591 suppression found in Directory.Build.props`);
        cs1591Violations++;
    }
}
const srcCsproj = walk(path.join(ROOT, 'src')).filter(f => f.endsWith('.csproj'));
for (const file of srcCsproj) {
    const content = fs.readFileSync(file, 'utf8');
    if (content.includes('CS1591') || content.includes('1591')) {
        logFail(`CS1591 suppression found in production project: ${path.relative(ROOT, file)}`);
        cs1591Violations++;
    }
}
for (const file of srcCsFiles) {
    const content = fs.readFileSync(file, 'utf8');
    if (content.includes('#pragma warning disable CS1591') || content.includes('#pragma warning disable 1591')) {
        logFail(`CS1591 pragma suppression in: ${path.relative(ROOT, file)}`);
        cs1591Violations++;
    }
}
if (cs1591Violations === 0) {
    logPass('CS1591 is not suppressed anywhere in production projects or Directory.Build.props (XML docs fully enforced).');
}

// 5. GitHub Repository & Username Integrity
console.log('\n\x1b[1m[5/7] GitHub Username & Repository Link Integrity\x1b[0m');
let linkViolations = 0;
const textFiles = walk(ROOT).filter(f => f.endsWith('.md') || f.endsWith('.cs') || f.endsWith('.props') || f.endsWith('.csproj'));
for (const file of textFiles) {
    const content = fs.readFileSync(file, 'utf8');
    // Match github.com/ericksonlopez (without the 'f' at the end)
    const match = content.match(/github\.com\/ericksonlopez\/(?:dotnet-|[A-Za-z])/g);
    if (match) {
        logFail(`Incorrect maintainer username 'ericksonlopez' (missing 'f') in: ${path.relative(ROOT, file)}`);
        linkViolations++;
    }
}
if (linkViolations === 0) {
    logPass('All repository references and GitHub author links correctly target @ericksonlopezf.');
}

// 6. NuGet Package Metadata & Branding Audit
console.log('\n\x1b[1m[6/7] NuGet Metadata & Branding Properties Validation\x1b[0m');
const propsPath = path.join(ROOT, 'Directory.Build.props');
const propsContent = fs.readFileSync(propsPath, 'utf8');
const requiredProps = [
    '<Authors>Erickson Lopez</Authors>',
    '<Company>Erickson Lopez</Company>',
    '<PackageLicenseExpression>MIT</PackageLicenseExpression>',
    '<PackageProjectUrl>https://ericksonlopez.dev/outbox</PackageProjectUrl>',
    '<RepositoryUrl>https://github.com/ericksonlopezf/dotnet-outbox</RepositoryUrl>',
    '<PackageReadmeFile>README.md</PackageReadmeFile>',
    '<PackageIcon>icon.png</PackageIcon>'
];

let metaViolations = 0;
for (const prop of requiredProps) {
    if (!propsContent.includes(prop)) {
        logFail(`Missing/invalid metadata property in Directory.Build.props: ${prop}`);
        metaViolations++;
    }
}
if (!fs.existsSync(path.join(ROOT, 'icon.png'))) {
    logFail('icon.png does not exist at root');
    metaViolations++;
}
if (metaViolations === 0) {
    logPass('Central Directory.Build.props contains complete, compliant NuGet metadata & icon.');
}

function removeStringsAndComments(src) {
    let out = '';
    let i = 0;
    let n = src.length;
    while (i < n) {
        if (src[i] === '/' && src[i+1] === '/') {
            while (i < n && src[i] !== '\n') i++;
            continue;
        }
        if (src[i] === '/' && src[i+1] === '*') {
            i += 2;
            while (i < n && !(src[i-1] === '*' && src[i] === '/')) i++;
            i++;
            continue;
        }
        if ((src[i] === '@' && src[i+1] === '"') || 
            (src[i] === '$' && src[i+1] === '@' && src[i+2] === '"') ||
            (src[i] === '@' && src[i+1] === '$' && src[i+2] === '"')) {
            i = src.indexOf('"', i) + 1;
            while (i < n) {
                if (src[i] === '"') {
                    if (src[i+1] === '"') {
                        i += 2;
                    } else {
                        i++;
                        break;
                    }
                } else {
                    i++;
                }
            }
            continue;
        }
        if (src[i] === '"' || (src[i] === '$' && src[i+1] === '"')) {
            i = src.indexOf('"', i) + 1;
            while (i < n) {
                if (src[i] === '\\') {
                    i += 2;
                } else if (src[i] === '"') {
                    i++;
                    break;
                } else {
                    i++;
                }
            }
            continue;
        }
        if (src[i] === "'") {
            i++;
            while (i < n) {
                if (src[i] === '\\') i += 2;
                else if (src[i] === "'") { i++; break; }
                else i++;
            }
            continue;
        }
        out += src[i];
        i++;
    }
    return out;
}

// 7. Top-Level One Type Per File Audit
console.log('\n\x1b[1m[7/7] One Type Per File (Top-Level) Validation\x1b[0m');
let typeViolations = 0;
for (const file of srcCsFiles) {
    const rawContent = fs.readFileSync(file, 'utf8');
    const isFileScoped = /^\s*namespace\s+[A-Za-z0-9_.]+\s*;/m.test(rawContent);
    const expectedTopLevelDepth = isFileScoped ? 0 : 1;

    const content = removeStringsAndComments(rawContent);
    const lines = content.split('\n');
    let depth = 0;
    let topTypes = [];
    for (let i = 0; i < lines.length; i++) {
        const line = lines[i];
        const trimmed = line.trim();
        const match = /^(?:public|internal|protected|private)?\s*(?:static\s+|sealed\s+|abstract\s+|readonly\s+|partial\s+)*(class|struct|interface|enum|record(?:\s+struct)?)\s+([A-Za-z0-9_]+)/.exec(trimmed);
        if (match && depth === expectedTopLevelDepth && !trimmed.startsWith('where ') && !trimmed.startsWith('new()')) {
            topTypes.push({ type: match[1], name: match[2], line: i + 1 });
        }
        for (const char of line) {
            if (char === '{') depth++;
            else if (char === '}') depth--;
        }
    }
    if (topTypes.length > 1) {
        logFail(`Multiple top-level types in ${path.relative(ROOT, file)}: ${topTypes.map(t => t.name).join(', ')}`);
        typeViolations++;
    }
}
if (typeViolations === 0) {
    logPass('100% of production C# source files adhere to the One Type Per File standard.');
}

// 8. Changelog Structure & Quadruple Versioning Synchronization Audit
console.log('\n\x1b[1m[8/10] Changelog Structure & Version Synchronization Validation\x1b[0m');
let changelogViolations = 0;
const changelogPath = fs.existsSync(path.join(ROOT, 'CHANGELOG.md')) ? path.join(ROOT, 'CHANGELOG.md') : (fs.existsSync(path.join(ROOT, 'changelog.md')) ? path.join(ROOT, 'changelog.md') : null);

if (!changelogPath) {
    logFail('Missing CHANGELOG.md or changelog.md in repository root.');
    changelogViolations++;
} else {
    const clContent = fs.readFileSync(changelogPath, 'utf8');
    const clLines = clContent.split('\n');
    let hasUnreleased = false;
    let firstReleaseVersion = null;
    let firstReleaseDate = null;

    for (let i = 0; i < clLines.Count || i < clLines.length; i++) {
        const line = clLines[i].trim();
        if (/^##\s*\[Unreleased\]\s*$/i.test(line)) {
            hasUnreleased = true;
            continue;
        }
        if (hasUnreleased) {
            const m = /^##\s*\[(\d+\.\d+\.\d+)\]\s*-\s*(\d{4}-\d{2}-\d{2})\s*$/.exec(line);
            if (m) {
                firstReleaseVersion = m[1];
                firstReleaseDate = m[2];
                break;
            }
        }
    }

    if (!hasUnreleased) {
        logFail("Changelog must strictly contain '## [Unreleased]' section at the top.");
        changelogViolations++;
    }

    if (!firstReleaseVersion) {
        logFail("Changelog first release under [Unreleased] must strictly match '## [VERSION] - AAAA-MM-DD' (e.g. '## [2.0.0] - 2026-08-30').");
        changelogViolations++;
    }

    // Extract VersionPrefix from Directory.Build.props
    let versionPrefix = null;
    if (fs.existsSync(rootPropsPath)) {
        const propsRaw = fs.readFileSync(rootPropsPath, 'utf8');
        const vMatch = /<VersionPrefix>\s*([^<\s]+)\s*<\/VersionPrefix>/.exec(propsRaw);
        if (vMatch) {
            versionPrefix = vMatch[1];
        }
    }

    if (!versionPrefix) {
        logFail('Could not extract <VersionPrefix> from Directory.Build.props.');
        changelogViolations++;
    } else if (firstReleaseVersion && firstReleaseVersion !== versionPrefix) {
        logFail(`Version mismatch: Directory.Build.props <VersionPrefix> (${versionPrefix}) does not match changelog latest version (${firstReleaseVersion}).`);
        changelogViolations++;
    }

    // Verify .release-please-manifest.json synchronization
    const manifestPath = path.join(ROOT, '.release-please-manifest.json');
    if (fs.existsSync(manifestPath)) {
        try {
            const manifestJson = JSON.parse(fs.readFileSync(manifestPath, 'utf8'));
            const manifestVer = manifestJson['.'];
            if (manifestVer && versionPrefix && manifestVer !== versionPrefix) {
                logFail(`Version mismatch: .release-please-manifest.json (${manifestVer}) does not match <VersionPrefix> (${versionPrefix}).`);
                changelogViolations++;
            }
        } catch (e) {
            logFail(`Failed to parse .release-please-manifest.json: ${e.message}`);
            changelogViolations++;
        }
    } else {
        logFail('Missing .release-please-manifest.json in repository root.');
        changelogViolations++;
    }
}

if (changelogViolations === 0) {
    logPass('Changelog structure (## [Unreleased] + ## [VERSION] - AAAA-MM-DD), <VersionPrefix> parity, and release manifest synchronized.');
}

// 9. Prohibition of ./local-packages & Local Package Feeds Audit
console.log('\n\x1b[1m[9/10] Prohibition of ./local-packages & Local Package Feeds Validation\x1b[0m');
let feedViolations = 0;
const localPkgDirs = walk(ROOT).filter(f => path.basename(f) === 'local-packages' && fs.statSync(f).isDirectory());
if (localPkgDirs.length > 0) {
    for (const d of localPkgDirs) {
        logFail(`Prohibited directory detected: ${path.relative(ROOT, d)}`);
        feedViolations++;
    }
}

const nugetConfigPath = path.join(ROOT, 'nuget.config');
if (fs.existsSync(nugetConfigPath)) {
    const nugetContent = fs.readFileSync(nugetConfigPath, 'utf8');
    if (/key="local-packages"/i.test(nugetContent) || /value="[.\\/]+local-packages"/i.test(nugetContent)) {
        logFail('Prohibited local package feed "local-packages" detected in nuget.config.');
        feedViolations++;
    }
    if (/<add\s+[^>]*value="(\.[\\/]|[a-zA-Z]:\\)/i.test(nugetContent) && !/https?:\/\//i.test(nugetContent)) {
        logFail('Local directory package source found in nuget.config. Only official remote feeds allowed.');
        feedViolations++;
    }
}

if (feedViolations === 0) {
    logPass('Prohibition of ./local-packages and local feeds verified. Packages resolve directly via NuGet.org.');
}

// 10. Explicit Usings & PackageProjectUrl Integrity Audit
console.log('\n\x1b[1m[10/10] Explicit Imports (ImplicitUsings=disable) & PackageProjectUrl Validation\x1b[0m');
let configViolations = 0;
if (fs.existsSync(rootPropsPath)) {
    const propsRaw = fs.readFileSync(rootPropsPath, 'utf8');
    if (!/<ImplicitUsings>\s*disable\s*<\/ImplicitUsings>/.test(propsRaw)) {
        logFail("Directory.Build.props must specify '<ImplicitUsings>disable</ImplicitUsings>'.");
        configViolations++;
    }
    if (/<PackageProjectUrl>[^<]*dotnet-[^<]*<\/PackageProjectUrl>/.test(propsRaw)) {
        logFail("PackageProjectUrl must NOT contain 'dotnet-' prefix. Required: https://ericksonlopez.dev/{lib}.");
        configViolations++;
    }
    if (!/<PackageProjectUrl>\s*https:\/\/ericksonlopez\.dev\/outbox\s*<\/PackageProjectUrl>/.test(propsRaw)) {
        logFail("PackageProjectUrl must be 'https://ericksonlopez.dev/outbox'.");
        configViolations++;
    }
}

const allCsprojs = walk(ROOT).filter(f => f.endsWith('.csproj'));
for (const cp of allCsprojs) {
    const content = fs.readFileSync(cp, 'utf8');
    if (/<ImplicitUsings>\s*(enable|true)\s*<\/ImplicitUsings>/i.test(content)) {
        logFail(`Prohibited <ImplicitUsings>enable</ImplicitUsings> in: ${path.relative(ROOT, cp)}`);
        configViolations++;
    }
    if (/<PackageProjectUrl>[^<]*dotnet-[^<]*<\/PackageProjectUrl>/i.test(content)) {
        logFail(`Prohibited 'dotnet-' prefix in PackageProjectUrl in: ${path.relative(ROOT, cp)}`);
        configViolations++;
    }
}

if (configViolations === 0) {
    logPass('ImplicitUsings=disable enforced globally and PackageProjectUrl strictly free of "dotnet-" prefix.');
}

console.log('\n=============================================================');
if (failureCount === 0) {
    console.log('  \x1b[32m\x1b[1m✨ ALL COMPLIANCE CHECKS PASSED PERFECTLY (0 FAILURES)\x1b[0m');
    console.log('=============================================================\n');
    process.exit(0);
} else {
    console.error(`  \x1b[31m\x1b[1m🚨 COMPLIANCE FAILED: ${failureCount} VIOLATIONS FOUND\x1b[0m`);
    console.log('=============================================================\n');
    process.exit(1);
}
