﻿using System;
using System.Linq;
using LibGit2Sharp;
using RepoZ.Api.Git;
using RepoZ.Api.Common.Common;

namespace RepoZ.Api.Common.Git
{
	public class DefaultRepositoryWriter : IRepositoryWriter
	{
		private readonly IGitCommander _gitCommander;
		private readonly IAppSettingsService _appSettingsService;

		public DefaultRepositoryWriter(IGitCommander gitCommander, IAppSettingsService appSettingsService)
		{
			_gitCommander = gitCommander ?? throw new ArgumentNullException(nameof(gitCommander));
			_appSettingsService = appSettingsService ?? throw new ArgumentNullException(nameof(appSettingsService));
		}

		public bool Checkout(Api.Git.Repository repository, string branchName)
		{
           
            using (var repo = new LibGit2Sharp.Repository(repository.Path))
            {
                string realBranchName = branchName;
                Branch branch;

                // Check if local branch exists
                if (repo.Branches.Any(b => b.FriendlyName == branchName))
                {
                    branch = Commands.Checkout(repo, branchName);
                }
                else
                {
                    // Create local branch to remote branch tip and set its upstream branch to remote
                    var upstreamBranch = repo.Branches.FirstOrDefault(b => b.FriendlyName.EndsWith(branchName));
                    branch = repo.CreateBranch(branchName, upstreamBranch.Tip);     
                    this.SetUpstream(repository, branchName, upstreamBranch.FriendlyName);

                    branch = Commands.Checkout(repo, branchName);
                }


                return branch.FriendlyName == branchName;
            }

		}

		public void Fetch(Api.Git.Repository repository)
		{
            var arguments = _appSettingsService.PruneOnFetch
                ? new string[] { "fetch", "--all", "--prune" }
                : new string[] { "fetch", "--all" };

			_gitCommander.Command(repository, arguments);
		}

		public void Pull(Api.Git.Repository repository)
		{
			_gitCommander.Command(repository, "pull");
		}

		public void Push(Api.Git.Repository repository)
		{
			_gitCommander.Command(repository, "push");
		}
        private void SetUpstream(Api.Git.Repository repository, string localBranchName, string upstreamBranchName)
        {
            _gitCommander.Command(repository, "branch", $"--set-upstream-to={upstreamBranchName}", localBranchName);
        }
    }
}
