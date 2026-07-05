using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Nuke.Common;
using Nuke.Common.CI.GitHubActions;
using Nuke.Common.Git;
using Nuke.Common.IO;
using Nuke.Common.Tools.GitHub;
using Octokit;
using Serilog;

namespace Components;

public interface ICreateRelease : INukeBuild {
    public const string GitHubRelease = nameof(GitHubRelease);

    [GitRepository] [Required] GitRepository GitRepository => TryGetValue(() => GitRepository);
    [Parameter] [Secret] string GitHubToken => TryGetValue(() => GitHubToken) ?? GitHubActions.Instance?.Token;

    string Name { get; }
    bool Prerelease => false;
    bool Draft => false;

    IEnumerable<AbsolutePath> AssetFiles { get; }

    Target CreateGitHubRelease => _ => _
        .Requires(() => GitHubToken)
        .Executes(async () => {
            GitHubTasks.GitHubClient.Credentials = new Credentials(GitHubToken.NotNull());

            var release = await GetOrCreateRelease();
            var uploadTasks = AssetFiles.Select(async x => {
                await using var assetFile = File.OpenRead(x);
                var asset = new ReleaseAssetUpload { 
                    FileName = x.Name, 
                    ContentType = "application/octet-stream", 
                    RawData = assetFile
                };
                await GitHubTasks.GitHubClient.Repository.Release.UploadAsset(release, asset);
            }).ToArray();

            Task.WaitAll(uploadTasks);
            return;

            async Task<Release> GetOrCreateRelease() {
                try {
                    return await GitHubTasks.GitHubClient.Repository.Release.Create(
                        GitRepository.GetGitHubOwner(),
                        GitRepository.GetGitHubName(),
                        new NewRelease(Name)
                        {
                            Name = Name,
                            Prerelease = Prerelease,
                            Draft = Draft,
                            Body = ""//ChangelogTasks.ExtractChangelogSectionNotes(ChangelogFile).JoinNewLine()
                        });

                }
                catch(Exception ex) {
                    Log.Error(ex, "Failed to create release");
                    
                    return await GitHubTasks.GitHubClient.Repository.Release.Get(
                        GitRepository.GetGitHubOwner(),
                        GitRepository.GetGitHubName(),
                        Name);
                }
            }
        });
}