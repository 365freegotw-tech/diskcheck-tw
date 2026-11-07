# SignPath Foundation signing draft

**Keep this workflow disabled until SignPath Foundation approves the project.**
`.github/workflows/release-signed.yml` runs only when the repository variable
`SIGNPATH_ENABLED` is exactly `true`. It builds and signs on a GitHub-hosted
Windows runner. Each signing request waits for a human approval in SignPath.
There are two requests per run: application files, then the NSIS installer.

## Activation checklist

1. Create a SignPath project for the public GitHub repository. Set its
   **Repository URL** to the exact public repository URL. Record the organization
   ID and project slug. Link the SignPath **GitHub.com trusted build system** to
   this project, and grant the API token's user submitter access.
2. Create two artifact configurations and record their slugs. Suggested slugs
   are `app-folder` and `installer`. GitHub `upload-artifact` supplies a ZIP
   archive; therefore both configurations need `<zip-file>` at the root.
   The upload paths in this workflow put the application files and installer
   executable at the archive root. The following are starting configurations
   to enter and validate in SignPath:

   Application (`app-folder`):

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="DiskCheckTW.App.exe">
         <authenticode-sign />
       </pe-file>
       <pe-file path="DiskCheckTW.Core.dll">
         <authenticode-sign />
       </pe-file>
     </zip-file>
   </artifact-configuration>
   ```

   Installer (`installer`):

   ```xml
   <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
     <zip-file>
       <pe-file path="DiskCheckTW-*-setup.exe">
         <authenticode-sign />
       </pe-file>
     </zip-file>
   </artifact-configuration>
   ```

   The installer pattern matches this repository's `-setup.exe` naming. If
   naming changes to `.setup.exe`, change the pattern to `*.setup.exe`.
3. Create a release signing policy using the Foundation certificate. Enable
   trusted build system verification, **Verify origin**, and **Use approval
   process** with at least one required human approval for every request.
   Restrict **Allowed branch names** to `main` and `refs/tags/v*` (confirm the
   tag pattern in the SignPath UI). Origin verification must accept only the
   configured repository, branch/tag, commit, and GitHub build job URL. Keep
   build settings in source control; do not allow manual build-setting overrides
   or reuse caches from previous builds. Record the signing policy slug.
4. In GitHub Actions settings, add this **secret**:
   `SIGNPATH_API_TOKEN`. Add these **repository variables**:
   `SIGNPATH_ORGANIZATION_ID`, `SIGNPATH_PROJECT_SLUG`,
   `SIGNPATH_SIGNING_POLICY_SLUG`,
   `SIGNPATH_APP_ARTIFACT_CONFIGURATION_SLUG`, and
   `SIGNPATH_INSTALLER_ARTIFACT_CONFIGURATION_SLUG`. Set the two artifact
   configuration variables to the slugs created in step 2. Do not add
   `SIGNPATH_ENABLED` yet.
5. Before a signed tag, remove the unsigned GitHub Release publishing path
   from the existing `build.yml` in a separate reviewed change. That workflow
   currently also runs on `v*` tags and publishes unsigned ZIPs to the same
   release; leaving it active would mix signed and unsigned downloads. This
   draft intentionally does not change `build.yml`.
6. Update the application and installer version metadata and existing release
   filenames for the new version. The first signed version is suggested as
   **v0.1.4**. Confirm that the public `main` contains this workflow and the
   version changes; the local `master` and isolated `public` branch are
   separate. Then set `SIGNPATH_ENABLED` to `true`. Use a `v*` tag on the
   approved public `main`, or manually dispatch from `main` to check signing
   without creating a GitHub Release.
7. Review the two SignPath requests and their verified origin details before
   approving each. Confirm the final portable ZIP contains the signed
   `DiskCheckTW.App.exe` and `DiskCheckTW.Core.dll`, and the setup ZIP contains
   the signed installer. A denied or timed-out request fails the workflow and
   prevents release publishing.

The workflow uses no action or .NET cache. It invokes
`build/publish.sh --publish-only`, which uses the same `dotnet publish`
arguments as the existing release build and generates the NSIS uninstall list.
The default `build/publish.sh` behavior remains unchanged.

References: [SignPath GitHub Actions integration](https://docs.signpath.io/trusted-build-systems/github),
[artifact configuration syntax](https://docs.signpath.io/artifact-configuration/syntax),
[origin verification](https://docs.signpath.io/origin-verification/), and
[project signing policies](https://docs.signpath.io/projects).
