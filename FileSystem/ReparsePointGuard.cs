namespace CoderCommander.FileSystem;

/// <summary>
/// The one flag that keeps a recursive directory walk inside the tree it was asked to walk.
///
/// <para><b>Empirically verified, not theoretical.</b> A junction (or a symlinked directory)
/// placed inside a tree being walked recursively is followed by .NET's directory enumeration by
/// default - confirmed by creating a real junction and watching <see cref="EnumerationOptions"/>
/// list a file that lives at the junction's target, outside the tree entirely, with
/// <c>AttributesToSkip</c> left at its default. Setting
/// <c>AttributesToSkip = ReparsePointGuard.SkipRecursion</c> (OR it in if something else is
/// already being skipped) stops the walk at the reparse point instead of following it - also
/// confirmed empirically against the same junction.</para>
///
/// <para><b>Why this matters more than it looks.</b> A directory a user selects for one operation
/// is not an invitation to touch whatever a junction inside it happens to point at. Three call
/// sites in this codebase recursed through a junction before this flag existed, each with a
/// different consequence, all reproduced with a real junction:</para>
/// <list type="bullet">
/// <item><see cref="Operations.WipeOperation"/>'s directory walk overwrote the linked target's
/// file <i>contents</i> with zeros - irreversible, and the entire point of a secure wipe is
/// exactly that irreversibility, applied here to files the user never selected.</item>
/// <item><c>PropertiesForm</c>'s recursive attribute/timestamp apply rewrote metadata (read-only
/// flag, last-write time) on files reachable only through the link.</item>
/// <item><c>MainForm.CopyDirectoryRecursive</c> (folder sync) copied the linked target's content
/// into the destination tree, materializing files that were never part of the source folder.</item>
/// </list>
///
/// <para>Deliberately a single named constant rather than a hand-rolled recursive walker: .NET's
/// own enumeration already has a tested, correct way to stop at a reparse point. Reinventing
/// directory recursion by hand to add one check would be a larger and riskier change than
/// composing the option that already exists.</para>
///
/// <para><b>Scope.</b> Only recursive walks need this. A single-level listing - showing a
/// junction as one clickable item, the way Explorer does - is unaffected and should stay that
/// way; entering it is then an explicit choice, not something a recursive operation did on the
/// user's behalf.</para>
/// </summary>
public static class ReparsePointGuard
{
    /// <summary>OR this into an <see cref="EnumerationOptions.AttributesToSkip"/> mask (or assign
    /// it directly when nothing else is being skipped) on every recursive directory walk.</summary>
    public const FileAttributes SkipRecursion = FileAttributes.ReparsePoint;
}
