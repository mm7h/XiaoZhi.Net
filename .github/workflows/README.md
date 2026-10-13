# XiaoZhi.Net Release workflow

This workflow prepares a draft from an existing version tag. The tag is required; it selects both the source commit and the versions of all own assemblies in the package. GitHub Actions builds Windows and Linux x64/ARM64 packages, checks compiled assembly versions and packaged OTA startup, then uploads four ZIP files and SHA256SUMS to an unpublished draft.

Run workflow becomes available after this CI-only change is merged into the default branch. Branch selects the workflow version; tag selects application source. The tag must contain the current publishing profiles, deployment scripts and package documentation, which are currently on develop. This PR does not merge those application changes into main. Review and merge application changes separately before tagging main for a new release.

Accepted tags: 0.2.0, v0.2.0, 0.2.0-rc.1. Product version removes v; numeric assembly/file versions use 0.2.0.0. Prerelease suffixes remain in the product version. The workflow does not commit version changes back to source.

For initial verification on develop, only tags matching 0.0.0-ci-release-test.* automatically trigger this workflow and create an unpublished prerelease draft. Existing published releases and resources are rejected. Once merged, use Actions → Release packages → Run workflow, enter an existing tag, then review and manually publish the resulting draft.

Rerun draft to replace the same five assets while preserving title, notes and unrelated assets. If artifacts have expired, rerun the entire workflow. If helper scripts are fixed, start a new run using the fixed workflow branch; rerunning an old run uses its original workflow commit.

The five GitHub official actions are pinned to full commit SHA values. A repository configured to allow only owner-local actions must explicitly allow these actions before the workflow can start.
