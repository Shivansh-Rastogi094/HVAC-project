using HvacConnect.Core.Domain.Entities;
using HvacConnect.Core.Domain.Enums;
using HvacConnect.Core.Services;
using HvacConnect.Core.Validation;
using HvacConnect.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace HvacConnect.ConsoleApp;

public static partial class Program
{
    private static async Task ShowLoginMenu(HvacDbContext db)
    {
        Console.WriteLine("\n─── SELECT USER TO LOGIN / REGISTER ───");
        Console.WriteLine("1. Super Admin (Shivansh Rastogi - Parent Group)");
        Console.WriteLine("2. Vendor Admin (Jordan Lee - Pacific Climate)");
        Console.WriteLine("3. Service Coordinator (Alex Morgan - Dispatcher)");
        Console.WriteLine("4. Technician (Casey Reed - Field Tech)");
        Console.WriteLine("5. Facility Manager / Customer (Marcus Vance - Apex Retail)");
        Console.WriteLine("6. Custom Email & Password Login (PBKDF2 Salt Hash Check)");
        Console.WriteLine("7. Register New Customer / End User Account");
        Console.WriteLine("0. Exit Application");
        Console.Write("Enter choice: ");

        string? choice = Console.ReadLine()?.Trim();
        if (choice == "0")
        {
            Environment.Exit(0);
        }
        else if (choice == "7")
        {
            await RegisterEndUserAccount(db);
            return;
        }

        string email;
        string? password = null;

        if (choice == "6")
        {
            email = PromptValidated("Enter email: ", InputValidator.ValidateEmail);
            Console.Write("Enter password: ");
            password = ReadMaskedPassword();
        }
        else
        {
            email = choice switch
            {
                "1" => "admin@hvacconnect.com",
                "2" => "jordan.lee@pacificclimate.com",
                "3" => "coordinator@hvacconnect.com",
                "4" => "casey.reed@pacificclimate.com",
                "5" => "m.vance@apexretail.example",
                _ => ""
            };
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            PrintError("Invalid selection.");
            return;
        }

        var user = await db.Users
            .Include(u => u.Vendor)
            .Include(u => u.Customer)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

        if (user == null)
        {
            PrintError($"User account '{email}' not found (401 Unauthorized).");
            return;
        }

        if (!user.IsActive)
        {
            PrintError("Account is deactivated. Access restricted (403 Forbidden).");
            return;
        }

        if (user.LockoutEndAt.HasValue && user.LockoutEndAt.Value > DateTime.UtcNow)
        {
            PrintError($"Account is temporarily locked due to consecutive failed attempts until {user.LockoutEndAt.Value:HH:mm UTC}.");
            return;
        }

        // Custom Login: Verify password against PBKDF2 salted hash with 5-attempt lockout
        if (choice == "6")
        {
            bool isPasswordCorrect = PasswordService.VerifyPassword(password ?? "", user.PasswordHash);
            if (!isPasswordCorrect)
            {
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= 5)
                {
                    user.LockoutEndAt = DateTime.UtcNow.AddMinutes(15);
                    PrintError("Too many failed login attempts. Account locked for 15 minutes (USR-BR-009).");
                }
                else
                {
                    PrintError($"Invalid credentials. Failed attempts: {user.FailedLoginCount}/5.");
                }
                await db.SaveChangesAsync();
                return;
            }

            user.FailedLoginCount = 0;
            user.LastLoginAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        _currentUser = user;
        PrintSuccess($"Logged in as: {user.FirstName} {user.LastName} | Role: [{user.Role}]" +
                     (user.Vendor != null ? $" | Vendor: {user.Vendor.BrandName}" : "") +
                     (user.Customer != null ? $" | Customer: {user.Customer.CustomerName}" : ""));
    }

    private static string ReadMaskedPassword()
    {
        string pwd = "";
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }
            if (key.Key == ConsoleKey.Backspace && pwd.Length > 0)
            {
                pwd = pwd[..^1];
                Console.Write("\b \b");
            }
            else if (!char.IsControl(key.KeyChar))
            {
                pwd += key.KeyChar;
                Console.Write("*");
            }
        }
        return pwd;
    }

    private static async Task RegisterEndUserAccount(HvacDbContext db)
    {
        Console.WriteLine("\n═══════ CUSTOMER SIGN-UP (END USER) ═══════");
        Console.WriteLine("Creates an active End User account linked with a new canonical customer profile.");

        string firstName = PromptValidated("First Name: ", val => InputValidator.ValidateRequiredText(val, "First Name", 2, 75));
        string lastName = PromptValidated("Last Name: ", val => InputValidator.ValidateRequiredText(val, "Last Name", 2, 75));
        string email = PromptValidated("Work Email: ", InputValidator.ValidateEmail);
        string password = PromptValidated("Password (min 8 chars, Upper, Lower, Digit, Special): ", InputValidator.ValidatePassword);
        string companyName = PromptValidated("Organization / Customer Legal Name: ", val => InputValidator.ValidateRequiredText(val, "Organization Name", 2, 200));
        string address = PromptValidated("Billing Address: ", val => InputValidator.ValidateRequiredText(val, "Billing Address", 5, 255));

        bool emailExists = await db.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());
        if (emailExists)
        {
            PrintError($"An account with email '{email}' already exists.");
            return;
        }

        using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            string custCode = $"CUST-{new Random().Next(10000, 99999)}";
            var customer = new CanonicalCustomer
            {
                CanonicalCode = custCode,
                CustomerName = companyName,
                NormalizedName = DuplicateMatchingEngine.NormalizeCustomerName(companyName),
                CustomerType = CustomerType.Commercial,
                PrimaryContactName = $"{firstName} {lastName}",
                PrimaryContactEmail = email,
                BillingAddress = address,
                IsSelfRegistered = true,
                Status = CustomerStatus.Active
            };
            db.CanonicalCustomers.Add(customer);
            await db.SaveChangesAsync();

            var user = new User
            {
                Email = email,
                PasswordHash = PasswordService.HashPassword(password),
                FirstName = firstName,
                LastName = lastName,
                Role = UserRole.EndUser,
                CustomerId = customer.Id,
                IsActive = true
            };
            db.Users.Add(user);
            await db.SaveChangesAsync();

            await transaction.CommitAsync();

            PrintSuccess($"Customer account created! [Customer Code: {custCode}]. You can now log in with {email}.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            PrintError($"Failed to register customer: {ex.Message}");
        }
    }
}
