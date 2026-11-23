using HvacConnect.Core.Domain.Entities;
using HvacConnect.Core.Domain.Enums;
using HvacConnect.Core.Validation;
using HvacConnect.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HvacConnect.ConsoleApp;

public static partial class Program
{
    private static async Task RunImportWizard(HvacDbContext db) => await VendorImportPipeline.RunAsync(db, _currentUser!, Prompt, PrintError, PrintSuccess);

    private static async Task RunDuplicateReview(HvacDbContext db)
    {
        Console.WriteLine("\n=== IMPORT DUPLICATE / CHANGE REVIEW ===");
        var candidates = await db.DuplicateCandidates.Include(d => d.ImportJob).ThenInclude(j => j!.Vendor)
            .Include(d => d.ImportRow).Include(d => d.ExistingCanonicalCustomer)
            .Where(d => d.Decision == DuplicateDecision.PendingReview).OrderBy(d => d.CreatedAt).ToListAsync();
        if (candidates.Count == 0) { Console.WriteLine("No pending duplicate reviews."); return; }

        foreach (var candidate in candidates)
        {
            var data = VendorImportPipeline.Deserialize(candidate.ImportRow?.MappedDataJson);
            string existing = candidate.ExistingCanonicalCustomer?.CustomerName ?? "(customer not found)";
            if (candidate.EntityType == "Sites")
                existing = await VendorImportPipeline.ExistingSiteLabelAsync(db, candidate, data) ?? existing;
            else if (candidate.EntityType == "Assets")
                existing = await VendorImportPipeline.ExistingAssetLabelAsync(db, candidate, data) ?? existing;

            Console.WriteLine($"\n[{candidate.DuplicateCode}] {candidate.EntityType} from {candidate.ImportJob?.Vendor?.BrandName} / {candidate.ImportJob?.OriginalFileName}, row {candidate.ImportRow?.RowNumber}");
            Console.WriteLine($"Imported: {candidate.CandidateName}");
            Console.WriteLine($"Existing: {existing}");
            Console.WriteLine($"Match: {candidate.ConfidenceScore}% ({candidate.ConfidenceLevel}) — {candidate.MatchReason}");
            Console.WriteLine("[A] Accept imported changes | [R] Reject changes and keep existing | [N] Keep as separate record | [S] Skip row");
            string action = Prompt("Decision: ").Trim().ToUpperInvariant();
            if (action is not ("A" or "R" or "N" or "S")) { PrintError("Invalid choice; leaving this item pending."); continue; }
            string notes = PromptValidated("Review notes: ", val => InputValidator.ValidateRequiredText(val, "Review notes", 3, 500));
            var vendor = candidate.ImportJob == null ? null : await db.Vendors.FindAsync(candidate.ImportJob.VendorId);

            if (action == "A")
            {
                await VendorImportPipeline.AcceptAsync(db, candidate, data, vendor);
                candidate.Decision = DuplicateDecision.LinkedToCustomer;
                candidate.ImportRow!.MatchOutcome = "ExistingChanged";
                candidate.ImportJob!.UpdatedRows++;
            }
            else if (action == "N")
            {
                await VendorImportPipeline.KeepSeparateAsync(db, candidate, data, vendor);
                candidate.Decision = DuplicateDecision.KeepSeparate;
                candidate.ImportRow!.MatchOutcome = "New";
            }
            else
            {
                candidate.Decision = DuplicateDecision.Skipped;
                candidate.ImportRow!.MatchOutcome = action == "R" ? "ExistingUnchanged" : "Skipped";
                candidate.ImportJob!.SkippedRows++;
            }
            candidate.ReviewNotes = notes;
            candidate.ReviewedByUserId = _currentUser!.Id;
            candidate.ReviewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        foreach (var job in candidates.Where(c => c.ImportJob != null).Select(c => c.ImportJob!).DistinctBy(j => j.Id))
        {
            bool pending = await db.DuplicateCandidates.AnyAsync(d => d.ImportJobId == job.Id && d.Decision == DuplicateDecision.PendingReview);
            job.Status = pending ? ImportJobStatus.DuplicateReview : ImportJobStatus.Committed;
            if (!pending) job.CompletedAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        PrintSuccess("Review decisions saved.");
    }

    private static async Task ViewOnboardingChecklists(HvacDbContext db)
    {
        var items = await db.VendorOnboardingChecklists.Include(c => c.Vendor).OrderBy(c => c.Phase).ThenBy(c => c.SortOrder).ToListAsync();
        var rows = items.Select(i => new[] { i.Vendor?.BrandName ?? "All", i.Phase, i.IsCompleted ? "[X] Completed" : "[ ] Pending", i.TaskDescription }).ToList();
        TableFormatter.PrintTable("Vendor Onboarding Readiness Checklists", new[] { "Vendor", "Phase", "Status", "Task Requirement" }, rows, new[] { 24, 10, 16, 45 });
    }
}
