﻿using System.Linq;
using LibGit2Sharp;
using RepoZ.Api.Git;
using System.IO;
using System;
using System.Collections.Generic;

namespace RepoZ.Api.Common.Git
{
	public class DefaultRepositoryReader : IRepositoryReader
	{
		public Api.Git.Repository ReadRepository(string path)
		{
			if (string.IsNullOrEmpty(path))
				return Api.Git.Repository.Empty;

			string repoPath = LibGit2Sharp.Repository.Discover(path);
			if (string.IsNullOrEmpty(repoPath))
				return Api.Git.Repository.Empty;

			return ReadRepositoryWithRetries(repoPath, 3);

		}

		private Api.Git.Repository ReadRepositoryWithRetries(string repoPath, int maxRetries)
		{
			Api.Git.Repository repository = null;
			int currentTry = 1;

			while (repository == null && currentTry <= maxRetries)
			{
				try
				{
					repository = ReadRepositoryInternal(repoPath);
				}
				catch (LockedFileException)
				{
					if (currentTry >= maxRetries)
						throw;
					else
						System.Threading.Thread.Sleep(500);
				}

				currentTry++;
			}

			return repository;
		}

		private Api.Git.Repository ReadRepositoryInternal(string repoPath)
		{
			try
			{
				using (var repo = new LibGit2Sharp.Repository(repoPath))
				{
					var status = repo.RetrieveStatus();

					var workingDirectory = new DirectoryInfo(repo.Info.WorkingDirectory);

					var headDetails = GetHeadDetails(repo);

					return new Api.Git.Repository()
					{
						Name = workingDirectory.Name,
						Path = workingDirectory.FullName,
						Location = workingDirectory.Parent.FullName,
						Branches = repo.Branches.Select(b => b.FriendlyName).ToArray(),
						LocalBranches = repo.Branches.Where(b => !b.IsRemote).Select(b => b.FriendlyName).ToArray(),
						AllBranches = this.GetAllBranches(repo),
						CurrentBranch = headDetails.Name,
						CurrentBranchHasUpstream = !string.IsNullOrEmpty(repo.Head.UpstreamBranchCanonicalName),
						CurrentBranchIsDetached = headDetails.IsDetached,
						CurrentBranchIsOnTag = headDetails.IsOnTag,
						AheadBy = repo.Head.TrackingDetails?.AheadBy,
						BehindBy = repo.Head.TrackingDetails?.BehindBy,
						LocalUntracked = status?.Untracked.Count(),
						LocalModified = status?.Modified.Count(),
						LocalMissing = status?.Missing.Count(),
						LocalAdded = status?.Added.Count(),
						LocalStaged = status?.Staged.Count(),
						LocalRemoved = status?.Removed.Count(),
						LocalIgnored = status?.Ignored.Count(),
						RemoteUrls = this.BuildUrls(repo.Network?.Remotes, headDetails),
						StashCount = repo.Stashes?.Count() ?? 0
					};
				}
			}
			catch (Exception)
			{
				return Api.Git.Repository.Empty;
			}
		}

		private string[] GetAllBranches(LibGit2Sharp.Repository repo)
		{
			var localBranches = repo.Branches.Where(b => !b.IsRemote).Select(b => b.FriendlyName).ToList();
			// "origin/" is removed from remote branches name and HEAD branch is ignored
			var strippedRemoteBranches = repo.Branches.Where(b => b.IsRemote && b.FriendlyName.IndexOf("HEAD") == -1).Select(b => b.FriendlyName.Replace("origin/", "")).ToList();

			var remoteOnlyBranches = strippedRemoteBranches.Except(localBranches);
			var localOnlyBranches = localBranches.Except(strippedRemoteBranches);
			var otherBranches = localBranches.Intersect(strippedRemoteBranches);

			// Merge branch lists and sort by name
			var allBranches = otherBranches
							  .Union(remoteOnlyBranches.Select(n => string.Concat(n, " (r)")))
							  .Union(localOnlyBranches.Select(n => string.Concat(n, " (l)")))
							  .OrderBy(n => n).ToArray();

			return allBranches;
		}

		private string[] BuildUrls(RemoteCollection remotes, HeadDetails headDetails)
		{
			List<string> urls = new List<string>();
			if (remotes != null)
			{
				//List<Remote> remotesList = remotes?.Where(r => !string.IsNullOrEmpty(r.Url) && !r.Url.StartsWith("git@")).ToList();
				foreach (var remote in remotes)
				{
					if (remote.Url.StartsWith("git@"))
					{
						urls.Add(Urlify(remote.Url, headDetails.Name, "/-/commits/", true));
						urls.Add(Urlify(remote.Url, headDetails.Name, "/-/branches/all/", false));

					}
					else
					{
						urls.Add(Urlify(remote.Url, headDetails.Name, "/tree/", true));
					}
				}

			}

			return urls.ToArray();
		}



		private String Urlify(string gitRemoteUrl, string currentBranch, string path, bool branch)
		{
			// Transformer :
			// git@			git2.april.interne.fr:distribution / aep / tarificateur - aep - web.git
			// en
			// https://	    git2.april.interne.fr/distribution/aep/tarificateur-aep-web.git

			string url = gitRemoteUrl;

			if (gitRemoteUrl.StartsWith("git@"))
			{
				url = url.Replace(":", "/");
				url = url.Replace("git@", "https://");
				url = url.Replace(".git", "");
			}
			else
			{
				url = url.Replace(".git", "");
				url = string.Concat(url, path, branch ? currentBranch : "");
			}


			return url;
		}

		private HeadDetails GetHeadDetails(LibGit2Sharp.Repository repo)
		{
			// unfortunately, type DetachedHead is internal ...
			var isDetached = repo.Head.GetType().Name.EndsWith("DetachedHead", StringComparison.OrdinalIgnoreCase);

			Tag tag = null;

			var headTipSha = repo.Head.Tip?.Sha;
			if (isDetached && headTipSha != null)
				tag = repo.Tags.FirstOrDefault(t => t.Target?.Sha?.Equals(repo.Head.Tip.Sha) ?? false);

			return new HeadDetails()
			{
				Name = isDetached
					? tag?.FriendlyName ?? headTipSha ?? repo.Head.FriendlyName
					: repo.Head.FriendlyName,
				IsDetached = isDetached,
				IsOnTag = tag != null
			};
		}

		internal class HeadDetails
		{
			internal string Name { get; set; }
			internal bool IsDetached { get; set; }
			internal bool IsOnTag { get; set; }
		}
	}
}

