using HvacConnect.Core.Domain.Entities;
using HvacConnect.Core.Domain.Enums;
using HvacConnect.Core.Services;
using HvacConnect.Core.Validation;
using HvacConnect.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HvacConnect.ConsoleApp;

public static partial class Program
{
    private static async Task ListTicketsMenu(HvacDbContext db)
    {
        IQueryable<ServiceTicket> query = db.ServiceTickets
            .Include(t => t.Customer)
            .Include(t => t.SiteLocation)
            .Include(t => t.Asset)
            .Include(t => t.Vendor)
            .Include(t => t.RaisedByUser)
            .Include(t => t.AssignedTechnician)
            .Include(t => t.Comments);

        if (_currentUser!.Role == UserRole.VendorAdmin)
        {
            query = query.Where(t => t.VendorId == _currentUser.VendorId || 
                                    (t.RaisedByUser != null && t.RaisedByUser.VendorId == _currentUser.VendorId));
        }
        else if (_currentUser.Role == UserRole.Technician)
        {
            query = query.Where(t => t.AssignedTechnicianId == _currentUser.Id);
        }
        else if (_currentUser.Role == UserRole.EndUser)
        {
            query = query.Where(t => t.CanonicalCustomerId == _currentUser.CustomerId);
        }

        var tickets = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();

        var rows = new List<string[]>();
        foreach (var t in tickets)
        {
            var liveSla = t.TargetResolutionAt.HasValue
                ? SlaCalculator.ComputeState(t.CreatedAt, t.TargetResolutionAt.Value, t.ResolvedAt, DateTime.UtcNow)
                : SLAState.WithinSLA;

            string techName = t.AssignedTechnician != null 
                ? $"{t.AssignedTechnician.FirstName} {t.AssignedTechnician.LastName}" 
                : "Unassigned";

            rows.Add(new string[]
            {
                t.TicketNumber,
                t.Status.ToString(),
                $"[{liveSla}]",
                t.Priority.ToString(),
                t.Asset?.AssetTag ?? "N/A",
                techName,
                t.Customer?.CustomerName ?? "N/A",
                t.Title
            });
        }

        TableFormatter.PrintTable("Active Service Tickets & SLA Watchdog",
            new[] { "Ticket#", "Status", "SLA State", "Priority", "Asset", "Technician", "Customer", "Title" },
            rows,
            new[] { 10, 12, 12, 10, 12, 18, 22, 28 });

        Console.Write("\nEnter Ticket# to view details & act (or press ENTER to back): ");
        string? ticketNum = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(ticketNum))
        {
            var selected = tickets.FirstOrDefault(t => t.TicketNumber.Equals(ticketNum, StringComparison.OrdinalIgnoreCase));
            if (selected != null)
            {
                await ManageTicketDetails(db, selected);
            }
            else
            {
                PrintError($"Ticket '{ticketNum}' was not found in your tenant scope (404 Not Found).");
            }
        }
    }

    private static async Task ManageTicketDetails(HvacDbContext db, ServiceTicket ticket)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"\n═══════ TICKET DETAILS: {ticket.TicketNumber} ═══════");
        Console.ResetColor();
        Console.WriteLine($"Title:       {ticket.Title}");
        Console.WriteLine($"Description: {ticket.Description}");
        Console.WriteLine($"Customer:    {ticket.Customer?.CustomerName} (Site: {ticket.SiteLocation?.SiteName})");
        Console.WriteLine($"Asset:       {ticket.Asset?.AssetTag} ({ticket.Asset?.Category} - {ticket.Asset?.Manufacturer})");
        Console.WriteLine($"Assigned To: {(ticket.AssignedTechnician != null ? ticket.AssignedTechnician.FirstName + " " + ticket.AssignedTechnician.LastName + " (" + ticket.Vendor?.BrandName + ")" : "Unassigned")}");
        Console.WriteLine($"Status:      {ticket.Status} | Priority: {ticket.Priority}");
        Console.WriteLine($"Resolution Deadline: {ticket.TargetResolutionAt:yyyy-MM-dd HH:mm UTC}");

        Console.WriteLine("\n─── Comments & Diagnostic Thread ───");
        var comments = await db.TicketComments
            .Include(c => c.AuthorUser)
            .Where(c => c.TicketId == ticket.Id)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();

        foreach (var c in comments)
        {
            if (c.IsInternalOnly && _currentUser!.Role == UserRole.EndUser) continue;
            string authorRole = c.AuthorUser != null ? c.AuthorUser.Role.ToString() : "System";
            Console.WriteLine($"[{c.CreatedAt:HH:mm}] {c.AuthorUser?.FirstName} {c.AuthorUser?.LastName} [{authorRole} - {c.CommentType}]: {c.CommentText}");
        }

        Console.WriteLine("\nActions:");
        Console.WriteLine("[C] Add Comment");

        bool canUpdateStatus = _currentUser!.Role == UserRole.Technician || 
                               _currentUser.Role == UserRole.ServiceCoordinator || 
                               _currentUser.Role == UserRole.VendorAdmin || 
                               _currentUser.Role == UserRole.SuperAdmin;
        if (canUpdateStatus)
        {
            Console.WriteLine("[U] Update Status");
        }

        bool canAssign = _currentUser.Role == UserRole.SuperAdmin || 
                         _currentUser.Role == UserRole.ServiceCoordinator || 
                         _currentUser.Role == UserRole.VendorAdmin;
        if (canAssign)
        {
            Console.WriteLine("[A] Assign / Reassign Technician (Smart Matching)");
        }
        Console.WriteLine("[B] Back");
        Console.Write("Choose action: ");
        string? act = Console.ReadLine()?.Trim().ToUpper();

        if (act == "C")
        {
            string text = PromptValidated("Enter comment text: ", val => InputValidator.ValidateRequiredText(val, "Comment text", 2, 2000));

            bool isInternal = false;
            if (_currentUser.Role != UserRole.EndUser)
            {
                isInternal = Prompt("Internal comment only? (y/n): ").Equals("y", StringComparison.OrdinalIgnoreCase);
            }

            CommentType cType = _currentUser.Role switch
            {
                UserRole.SuperAdmin => CommentType.StatusChange,
                UserRole.VendorAdmin => CommentType.StatusChange,
                UserRole.ServiceCoordinator => CommentType.Assignment,
                UserRole.Technician => CommentType.Resolution,
                _ => CommentType.UserComment
            };

            var c = new TicketComment
            {
                TicketId = ticket.Id,
                AuthorUserId = _currentUser.Id,
                CommentText = text,
                CommentType = cType,
                IsInternalOnly = isInternal
            };
            db.TicketComments.Add(c);
            await db.SaveChangesAsync();
            PrintSuccess("Comment added to ticket thread.");
        }
        else if (act == "U" && canUpdateStatus)
        {
            Console.WriteLine($"Current Status: {ticket.Status}");
            Console.WriteLine("Allowed progression: Open -> Assigned -> InProgress -> Resolved -> Closed");
            Console.Write("Enter new status (Assigned, InProgress, Resolved, Closed): ");
            string? newStatusStr = Console.ReadLine()?.Trim();

            if (Enum.TryParse<TicketStatus>(newStatusStr, true, out var newStatus))
            {
                bool validMove = (ticket.Status == TicketStatus.Open && newStatus == TicketStatus.Assigned) ||
                                 (ticket.Status == TicketStatus.Assigned && newStatus == TicketStatus.InProgress) ||
                                 (ticket.Status == TicketStatus.InProgress && newStatus == TicketStatus.Resolved) ||
                                 (ticket.Status == TicketStatus.Resolved && newStatus == TicketStatus.Closed);

                if (!validMove)
                {
                    PrintError($"Invalid ticket status transition from '{ticket.Status}' to '{newStatus}'. Allowed: Open -> Assigned -> InProgress -> Resolved -> Closed (Rule TLC-RUL-002).");
                    return;
                }

                if (newStatus == TicketStatus.Resolved)
                {
                    string notes = PromptValidated("Mandatory Resolution Notes: ", val => InputValidator.ValidateRequiredText(val, "Resolution Notes", 5, 5000));
                    ticket.ResolutionNotes = notes;
                    ticket.ResolvedAt = DateTime.UtcNow;
                }
                else if (newStatus == TicketStatus.Closed)
                {
                    ticket.ClosedAt = DateTime.UtcNow;
                }

                ticket.Status = newStatus;

                db.TicketComments.Add(new TicketComment
                {
                    TicketId = ticket.Id,
                    AuthorUserId = _currentUser.Id,
                    CommentType = CommentType.StatusChange,
                    CommentText = $"Status updated to '{newStatus}' by {_currentUser.FirstName} {_currentUser.LastName} ({_currentUser.Role})."
                });

                await db.SaveChangesAsync();
                PrintSuccess($"Ticket status updated to {newStatus}.");
            }
            else
            {
                PrintError($"Invalid status name '{newStatusStr}'. Must be Assigned, InProgress, Resolved, or Closed.");
            }
        }
        else if (act == "A" && canAssign)
        {
            await AssignTechnicianSmartWizard(db, ticket);
        }
    }

    private static async Task AssignTechnicianSmartWizard(HvacDbContext db, ServiceTicket ticket)
    {
        Console.WriteLine("\n─── TECHNICIAN ASSIGNMENT MODULE ───");
        Console.WriteLine($"Ticket: {ticket.TicketNumber} | Subject Machine: {ticket.Asset?.Category} ({ticket.Asset?.Manufacturer})");

        IQueryable<User> techQuery = db.Users
            .Include(u => u.Vendor)
            .Where(u => u.Role == UserRole.Technician && u.IsActive);

        if (_currentUser!.Role == UserRole.VendorAdmin)
        {
            techQuery = techQuery.Where(u => u.VendorId == _currentUser.VendorId);
        }

        var allTechs = await techQuery.ToListAsync();
        if (!allTechs.Any())
        {
            PrintError("No active technicians available in scope to assign.");
            return;
        }

        var techIds = allTechs.Select(t => t.Id).ToList();
        var openTicketsPerTech = await db.ServiceTickets
            .Where(t => t.AssignedTechnicianId != null && techIds.Contains(t.AssignedTechnicianId.Value) && 
                        t.Status != TicketStatus.Closed && t.Status != TicketStatus.Resolved)
            .GroupBy(t => t.AssignedTechnicianId!.Value)
            .Select(g => new { TechId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.TechId, x => x.Count);

        string requiredCategory = ticket.Asset?.Category.ToString() ?? "";

        var rankedTechs = allTechs.OrderByDescending(t => 
            !string.IsNullOrEmpty(t.Specialisation) && 
            (t.Specialisation.Contains(requiredCategory, StringComparison.OrdinalIgnoreCase) || 
             (requiredCategory == "RooftopUnit" && t.Specialisation.Contains("HVAC", StringComparison.OrdinalIgnoreCase)) ||
             (requiredCategory == "Chiller" && t.Specialisation.Contains("Chiller", StringComparison.OrdinalIgnoreCase)) ||
             (requiredCategory.Contains("Cooler") && t.Specialisation.Contains("Refrigeration", StringComparison.OrdinalIgnoreCase)) ||
             (requiredCategory.Contains("Rack") && t.Specialisation.Contains("Refrigeration", StringComparison.OrdinalIgnoreCase))))
            .ThenBy(t => openTicketsPerTech.GetValueOrDefault(t.Id, 0))
            .ToList();

        var rows = new List<string[]>();
        foreach (var tech in rankedTechs)
        {
            int activeJobs = openTicketsPerTech.GetValueOrDefault(tech.Id, 0);
            bool isSpecMatch = !string.IsNullOrEmpty(tech.Specialisation) &&
                               (tech.Specialisation.Contains(requiredCategory, StringComparison.OrdinalIgnoreCase) ||
                                (requiredCategory == "RooftopUnit" && tech.Specialisation.Contains("HVAC", StringComparison.OrdinalIgnoreCase)));

            string specDisplay = tech.Specialisation ?? "General HVAC";
            if (isSpecMatch) specDisplay += " ⭐ [Best Fit]";

            rows.Add(new string[]
            {
                tech.Id.ToString(),
                $"{tech.FirstName} {tech.LastName}",
                specDisplay,
                $"{activeJobs} active",
                tech.AvailabilityStatus.ToString()
            });
        }

        TableFormatter.PrintTable("Ranked Technicians by Specialisation Match & Workload",
            new[] { "ID", "Name", "Specialisation", "Active Jobs", "Status" },
            rows,
            new[] { 6, 20, 26, 14, 14 });

        string chosenIdStr = Prompt("\nEnter Technician ID to assign (or press ENTER to cancel): ");
        if (int.TryParse(chosenIdStr, out int chosenId))
        {
            var chosenTech = rankedTechs.FirstOrDefault(t => t.Id == chosenId);
            if (chosenTech != null)
            {
                ticket.AssignedTechnicianId = chosenTech.Id;
                ticket.VendorId = chosenTech.VendorId;
                if (ticket.Status == TicketStatus.Open)
                {
                    ticket.Status = TicketStatus.Assigned;
                }
                ticket.RespondedAt ??= DateTime.UtcNow;

                db.TicketComments.Add(new TicketComment
                {
                    TicketId = ticket.Id,
                    AuthorUserId = _currentUser.Id,
                    CommentType = CommentType.Assignment,
                    CommentText = $"Assigned to {chosenTech.FirstName} {chosenTech.LastName} ({chosenTech.Vendor?.BrandName ?? "Independent"}) by {_currentUser.FirstName} {_currentUser.LastName}."
                });

                await db.SaveChangesAsync();
                PrintSuccess($"Ticket {ticket.TicketNumber} assigned to {chosenTech.FirstName} {chosenTech.LastName}.");
            }
            else
            {
                PrintError($"Technician ID {chosenId} was not found.");
            }
        }
    }

    private static async Task CreateTicketWizard(HvacDbContext db)
    {
        Console.WriteLine("\n─── RAISE SERVICE TICKET ───");

        IQueryable<SiteLocation> sitesQuery = db.SiteLocations.Include(s => s.Customer).Include(s => s.PrimaryVendor);
        if (_currentUser!.Role == UserRole.VendorAdmin)
        {
            sitesQuery = sitesQuery.Where(s => s.PrimaryVendorId == _currentUser.VendorId);
        }
        else if (_currentUser.Role == UserRole.EndUser)
        {
            sitesQuery = sitesQuery.Where(s => s.CanonicalCustomerId == _currentUser.CustomerId);
        }

        var sites = await sitesQuery.ToListAsync();
        if (!sites.Any())
        {
            PrintError("No locations available to raise a ticket for in your scope.");
            return;
        }

        Console.WriteLine("Select Location:");
        for (int i = 0; i < sites.Count; i++)
        {
            Console.WriteLine($"{i + 1}. [{sites[i].SiteCode}] {sites[i].SiteName} (Customer: {sites[i].Customer?.CustomerName})");
        }

        string sChoice = Prompt($"Select Site [1-{sites.Count}]: ");
        if (!int.TryParse(sChoice, out int siteIdx) || siteIdx < 1 || siteIdx > sites.Count)
        {
            PrintError("Invalid site selection.");
            return;
        }
        var selectedSite = sites[siteIdx - 1];

        var assets = await db.AssetEquipment.Where(a => a.SiteLocationId == selectedSite.Id).ToListAsync();
        if (!assets.Any())
        {
            PrintError($"No equipment assets are registered at '{selectedSite.SiteName}'. A ticket must reference a valid machine.");
            return;
        }

        Console.WriteLine("\nSelect Equipment Asset:");
        for (int i = 0; i < assets.Count; i++)
        {
            Console.WriteLine($"{i + 1}. [{assets[i].AssetTag}] {assets[i].Category} - {assets[i].Manufacturer} ({assets[i].ModelNumber})");
        }

        string aChoice = Prompt($"Select Asset [1-{assets.Count}]: ");
        if (!int.TryParse(aChoice, out int assetIdx) || assetIdx < 1 || assetIdx > assets.Count)
        {
            PrintError("Invalid asset selection.");
            return;
        }
        var selectedAsset = assets[assetIdx - 1];

        string title = PromptValidated("\nIssue Title (e.g. Chiller High Water Temp Alert): ", val => InputValidator.ValidateRequiredText(val, "Issue Title", 3, 200));
        string desc = PromptValidated("Description / Fault code: ", val => InputValidator.ValidateRequiredText(val, "Description", 5, 2000));

        Console.WriteLine("Priority: 1. Critical (4h SLA) | 2. High (8h SLA) | 3. Medium (24h SLA) | 4. Low (48h SLA)");
        string pChoice = Prompt("Select Priority [1-4]: ");

        TicketPriority priority = pChoice switch
        {
            "1" => TicketPriority.Critical,
            "2" => TicketPriority.High,
            "4" => TicketPriority.Low,
            _ => TicketPriority.Medium
        };

        var (respTime, resTime) = SlaCalculator.GetDefaultDurations(priority);
        string ticketNumber = $"TKT-{new Random().Next(2500, 9999)}";

        int? ticketVendorId = _currentUser.Role == UserRole.VendorAdmin 
            ? _currentUser.VendorId 
            : selectedSite.PrimaryVendorId;

        var ticket = new ServiceTicket
        {
            TicketNumber = ticketNumber,
            CanonicalCustomerId = selectedSite.CanonicalCustomerId,
            SiteLocationId = selectedSite.Id,
            VendorId = ticketVendorId,
            AssetEquipmentId = selectedAsset.Id,
            Title = title,
            Description = desc,
            Priority = priority,
            Status = TicketStatus.Open,
            RaisedByUserId = _currentUser.Id,
            TargetResponseAt = DateTime.UtcNow.Add(respTime),
            TargetResolutionAt = DateTime.UtcNow.Add(resTime),
            SLAState = SLAState.WithinSLA
        };

        db.ServiceTickets.Add(ticket);
        await db.SaveChangesAsync();

        db.TicketComments.Add(new TicketComment
        {
            TicketId = ticket.Id,
            AuthorUserId = _currentUser.Id,
            CommentType = CommentType.StatusChange,
            CommentText = $"Ticket raised by {_currentUser.FirstName} {_currentUser.LastName} ({_currentUser.Role})."
        });
        await db.SaveChangesAsync();

        PrintSuccess($"Ticket created: {ticketNumber} with Resolution SLA deadline: {ticket.TargetResolutionAt:yyyy-MM-dd HH:mm UTC}");
    }
}
