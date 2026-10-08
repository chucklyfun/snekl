using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Snekl.Commissions;
using Snekl.Commissions.Data;
using Snekl.Commissions.Services;
using Snekl.Commissions.Workflow;

namespace Snekl.Commissions.Tests;

public class CommissionAppServiceTests
{
    static (CommissionsDb Db, CommissionAppService App, MemberSession Artist, MemberSession Client) Create()
    {
        var options = new DbContextOptionsBuilder<CommissionsDb>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new CommissionsDb(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();

        var content = "http://localhost:5001";
        var artistUser = Guid.NewGuid();
        var clientUser = Guid.NewGuid();
        db.Studios.Add(new ArtistStudio
        {
            ContentBaseUrl = content,
            UserId = artistUser,
            Username = "artist",
            DisplayName = "Artist",
            AcceptingCommissions = true,
            CcBillClientAcc = "900000",
            CcBillSubacc = "0000",
            CcBillFlexFormId = "687fa3e0-e60d-4466-88e2-181fa56dd6a9",
            CcBillSalt = "7d901dad245fd0ff6bc20d06"
        });
        db.SaveChanges();

        var app = new CommissionAppService(db, Options.Create(new CommissionsOptions { PublicBaseUrl = "http://localhost:5301" }));
        var artist = new MemberSession { ContentBaseUrl = content, UserId = artistUser, Username = "artist", DisplayName = "Artist", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        var client = new MemberSession { ContentBaseUrl = content, UserId = clientUser, Username = "client", DisplayName = "Client", ExpiresAt = DateTime.UtcNow.AddHours(1) };
        return (db, app, artist, client);
    }

    [Fact]
    public async Task AdultListing_RejectsEscrowRequest()
    {
        var (db, app, artist, client) = Create();
        var listing = await app.CreateListingAsync(artist, new CreateListing("Adult piece", "desc", "fixed", 2000, 2, null, Adult: true), default);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.RequestAsync(client, listing.Id, new RequestCommission("please", CommissionModes.Escrow), default));
        Assert.Contains("Adult", ex.Message);
        db.Dispose();
    }

    [Fact]
    public async Task CcBill_AcceptCreatesMilestones_AndGatesProgress()
    {
        var (db, app, artist, client) = Create();
        var listing = await app.CreateListingAsync(artist, new CreateListing("Adult piece", "desc", "fixed", 2000, 2, null, Adult: true), default);
        var order = await app.RequestAsync(client, listing.Id, new RequestCommission("please", CommissionModes.CcBill), default);

        order = await app.TransitionAsync(artist, null, order.Id, CommissionStatuses.Accepted, new[]
        {
            new MilestoneDraft("Sketch", 800),
            new MilestoneDraft("Final", 1200)
        }, default);

        Assert.Equal(2, order.Milestones.Count);
        Assert.Equal(800, order.Milestones[0].AmountCents);

        var blocked = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            app.TransitionAsync(artist, null, order.Id, CommissionStatuses.InProgress, null, default));
        Assert.Contains("first CCBill milestone", blocked.Message);

        var digest = Payments.CcBillDigest.FormDigest(800, "7d901dad245fd0ff6bc20d06");
        var form = new Dictionary<string, string>
        {
            ["eventType"] = "NewSaleSuccess",
            ["transactionId"] = "txn_a",
            ["clientAccnum"] = "900000",
            ["clientSubacc"] = "0000",
            ["billedInitialPrice"] = "8.00",
            ["dynamicPricingValidationDigest"] = digest,
            ["snekl_order_id"] = order.Id.ToString("D"),
            ["snekl_milestone_id"] = order.Milestones[0].Id.ToString("D")
        };
        var (status, _) = await app.HandleCcBillWebhookAsync(form, default);
        Assert.Equal(200, status);

        order = await app.TransitionAsync(artist, null, order.Id, CommissionStatuses.InProgress, null, default);
        Assert.Equal(CommissionStatuses.InProgress, order.Status);

        var (dupStatus, dupBody) = await app.HandleCcBillWebhookAsync(form, default);
        Assert.Equal(200, dupStatus);
        Assert.Equal("duplicate", dupBody);

        db.Dispose();
    }

    [Fact]
    public async Task Studio_DoesNotReturnSalt()
    {
        var (db, app, artist, _) = Create();
        var studio = await app.UpsertStudioAsync(artist, new UpsertStudio(
            "Artist", "tos", true, "note", "900000", "0000", "flex-id", "super-secret-salt", null), default);
        Assert.True(studio.CcBillSaltSet);
        Assert.True(studio.CcBillReady);
        Assert.Contains("/api/ccbill/webhook", studio.CcBillWebhookUrl);
        var json = System.Text.Json.JsonSerializer.Serialize(studio);
        Assert.DoesNotContain("super-secret-salt", json);
        db.Dispose();
    }
}
