// SPDX-FileCopyrightText: 2025 Bazs
// SPDX-License-Identifier: GPL-3.0-or-later

using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using Entree.Core.Handler;
using Entree.MVVM.Model;
using Xunit;

namespace Entree.Tests;

public class CompareHandlerTests
{
   private static readonly SecurityIdentifier OwnerOld = new("S-1-5-18");
   private static readonly SecurityIdentifier OwnerNew = new("S-1-5-32-544");
   private static readonly SecurityIdentifier IdentityA = new("S-1-5-21-1-2-3-1001");
   private static readonly SecurityIdentifier IdentityB = new("S-1-5-21-1-2-3-1002");

   private static FlatFileSystemStruct Ace(string path, SecurityIdentifier? owner, SecurityIdentifier identity,
                                           FileSystemRights access) =>
            new()
            {
                     Path           = path,
                     Owner          = owner,
                     Identity       = identity,
                     Access         = access,
                     FileAttributes = FileAttributes.Normal,
                     LastWriteTime  = new DateTime(2026, 1, 1)
            };

   private static CompareHandler NewHandler() => new("", "", null!);

   [Fact]
   public void CompareFileSystemItem_OwnerChanged_StillMatchesIdentityOnLaterAce()
   {
      const string path = @"C:\data";
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>
                      {
                               [path] =
                               [
                                        Ace(path, OwnerOld, IdentityA, FileSystemRights.Read),
                                        Ace(path, OwnerOld, IdentityB, FileSystemRights.FullControl)
                               ]
                      };
      var comparison = Ace(path, OwnerNew, IdentityB, FileSystemRights.FullControl);

      var result = NewHandler().CompareFileSystemItem(originals, comparison);

      Assert.NotNull(result);
      Assert.DoesNotContain("New Identity", result.Difference);
      Assert.DoesNotContain("New Access", result.Difference);
      Assert.Contains("New Owner", result.Difference);
   }

   [Fact]
   public void CompareFileSystemItem_PathChangesOnlyOnTheFirstRow()
   {
      const string path = @"C:\data";
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>
                      {
                               [path] = [Ace(path, OwnerOld, IdentityA, FileSystemRights.Read)]
                      };
      var comparison = Ace(path, OwnerNew, IdentityA, FileSystemRights.Read);

      Assert.Contains("New Owner", NewHandler().CompareFileSystemItem(originals, comparison)!.Difference);
      Assert.Null(NewHandler().CompareFileSystemItem(originals, comparison, false));
   }

   [Fact]
   public void CompareFileSystemItem_IdenticalEntry_ReturnsNull()
   {
      const string path = @"C:\data";
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>
                      {
                               [path] = [Ace(path, OwnerOld, IdentityA, FileSystemRights.Read)]
                      };
      var comparison = Ace(path, OwnerOld, IdentityA, FileSystemRights.Read);

      Assert.Null(NewHandler().CompareFileSystemItem(originals, comparison));
   }

   [Fact]
   public void CompareFileSystemItem_NewIdentity_ReportedWhenTrulyAbsent()
   {
      const string path = @"C:\data";
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>
                      {
                               [path] = [Ace(path, OwnerOld, IdentityA, FileSystemRights.Read)]
                      };
      var comparison = Ace(path, OwnerOld, IdentityB, FileSystemRights.Read);

      var result = NewHandler().CompareFileSystemItem(originals, comparison);

      Assert.NotNull(result);
      Assert.Contains("New Identity", result.Difference);
      Assert.Null(result.Access);
      Assert.Equal(FileSystemRights.Read, result.NewAccess);
      Assert.True(result.Kind.HasFlag(DifferenceKind.Access));
   }

   [Fact]
   public void CompareFileSystemItem_PathMissingFromOriginals_ReturnsNull()
   {
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>();
      var comparison = Ace(@"C:\ghost", OwnerOld, IdentityA, FileSystemRights.Read);

      Assert.Null(NewHandler().CompareFileSystemItem(originals, comparison));
   }

   [Fact]
   public void CompareFileSystemItem_CapturesOnlyTheCategoriesThatChanged()
   {
      const string path = @"C:\data";
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>
                      {
                               [path] = [Ace(path, OwnerOld, IdentityA, FileSystemRights.Read)]
                      };
      var comparison = Ace(path, OwnerNew, IdentityA, FileSystemRights.FullControl);

      var result = NewHandler().CompareFileSystemItem(originals, comparison);

      Assert.NotNull(result);
      Assert.Equal(DifferenceKind.Access | DifferenceKind.Owner, result.Kind);
   }

   [Fact]
   public void CompareFileSystemItem_DenyForExistingAllowIdentity_IsNewIdentity()
   {
      const string path = @"C:\data";
      var originals = new Dictionary<string, List<FlatFileSystemStruct>>
                      {
                               [path] = [Ace(path, OwnerOld, IdentityA, FileSystemRights.Write)]
                      };
      var comparison = Ace(path, OwnerOld, IdentityA, FileSystemRights.Write) with
                       { AccessType = AccessControlType.Deny };

      var result = NewHandler().CompareFileSystemItem(originals, comparison);

      Assert.NotNull(result);
      Assert.Contains("New Identity", result.Difference);
      Assert.Equal(AccessControlType.Deny, result.AccessType);
   }

   [Fact]
   public void GroupByPath_MatchesPathsIgnoringCase()
   {
      var lookup = CompareHandler.GroupByPath([Ace(@"\\FS01\Share\Data", OwnerOld, IdentityA, FileSystemRights.Read)]);

      Assert.True(lookup.ContainsKey(@"\\fs01\share\data"));
   }

   [Fact]
   public void HasOwners_FalseWhenTheReportHasNoOwnerColumn()
   {
      var withoutOwners = CompareHandler.GroupByPath([Ace(@"C:\data", null, IdentityA, FileSystemRights.Read)]);
      var withOwners    = CompareHandler.GroupByPath([Ace(@"C:\data", OwnerOld, IdentityA, FileSystemRights.Read)]);

      Assert.False(CompareHandler.HasOwners(withoutOwners));
      Assert.True(CompareHandler.HasOwners(withOwners));
   }
}
