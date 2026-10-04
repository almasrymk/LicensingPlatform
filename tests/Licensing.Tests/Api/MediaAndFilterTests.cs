using System.Net;
using System.Net.Http.Headers;
using Licensing.Infrastructure.Persistence;
using Licensing.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Licensing.Tests.Api;

public sealed class MediaTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public MediaTests(TestAppFixture f) => _app = f.App;

    // Smallest valid PNG (1x1 transparent pixel).
    private static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    private static MultipartFormDataContent Form(byte[] bytes, string name = "logo.png", string type = "image/png")
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(type);
        return new MultipartFormDataContent { { content, "file", name } };
    }

    [Fact]
    public async Task Product_image_is_uploaded_served_and_listed()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var productId = await _app.WithDbAsync(db => db.Products.Where(p => p.Code == "POS").Select(p => p.Id).FirstAsync());

        var uploaded = await (await admin.PutAsync($"/api/v1/products/{productId}/image", Form(Png))).OkJsonAsync();
        var url = uploaded.GetProperty("url").GetString()!;

        var image = await _app.Anonymous().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, image.StatusCode);
        Assert.Equal("image/png", image.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await image.Content.ReadAsByteArrayAsync());

        var products = await (await admin.GetAsync("/api/v1/products?pageSize=50")).OkJsonAsync();
        var pos = products.GetProperty("items").EnumerateArray().First(p => p.GetProperty("code").GetString() == "POS");
        Assert.Equal(url, pos.GetProperty("imageUrl").GetString());

        // Replacing the image removes the old file.
        var second = await (await admin.PutAsync($"/api/v1/products/{productId}/image", Form(Png))).OkJsonAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await _app.Anonymous().GetAsync(url)).StatusCode);
        Assert.NotEqual(url, second.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Non_images_and_svg_are_rejected()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var customerId = await _app.WithDbAsync(db => db.Customers.Where(c => c.Name == "مطاعم السلطان").Select(c => c.Id).FirstAsync());
        var svg = "<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>"u8.ToArray();
        var response = await admin.PutAsync($"/api/v1/customers/{customerId}/image", Form(svg, "x.svg", "image/svg+xml"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("IMAGE_INVALID", await response.ErrorCodeAsync());
    }

    [Fact]
    public async Task Cannot_attach_an_image_to_another_tenants_record()
    {
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var school = await _app.WithDbAsync(db => db.Customers.Where(c => c.Name == "مدارس المنار").Select(c => c.Id).FirstAsync());
        var response = await nour.PutAsync($"/api/v1/customers/{school}/image", Form(Png));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Users_set_their_own_photo_which_appears_in_the_profile()
    {
        var user = await _app.LoginAsync(DemoAccounts.ShifaUser);
        await (await user.PutAsync("/api/v1/auth/me/image", Form(Png))).OkJsonAsync();
        var me = await (await user.GetAsync("/api/v1/auth/me")).OkJsonAsync();
        Assert.StartsWith("/api/v1/media/", me.GetProperty("imageUrl").GetString());
    }
}

public sealed class FilterTests : IClassFixture<TestAppFixture>
{
    private readonly TestApp _app;
    public FilterTests(TestAppFixture f) => _app = f.App;

    [Fact]
    public async Task License_filters_near_expiry_limit_and_product()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var near = await (await admin.GetAsync("/api/v1/licenses?nearExpiry=true&pageSize=100")).OkJsonAsync();
        Assert.All(near.GetProperty("items").EnumerateArray(), l =>
            Assert.True(l.GetProperty("expiresAt").GetDateTimeOffset() <= DateTimeOffset.UtcNow.AddDays(31)));

        var full = await (await admin.GetAsync("/api/v1/licenses?limitReached=true&pageSize=100")).OkJsonAsync();
        Assert.All(full.GetProperty("items").EnumerateArray(), l =>
            Assert.Equal(l.GetProperty("maxActivations").GetInt32(), l.GetProperty("activeActivations").GetInt32()));

        var posId = await _app.WithDbAsync(db => db.Products.Where(p => p.Code == "POS").Select(p => p.Id).FirstAsync());
        var pos = await (await admin.GetAsync($"/api/v1/licenses?productId={posId}&pageSize=100")).OkJsonAsync();
        Assert.NotEmpty(pos.GetProperty("items").EnumerateArray());
        Assert.All(pos.GetProperty("items").EnumerateArray(), l => Assert.Equal("POS", l.GetProperty("productCode").GetString()));
    }

    [Fact]
    public async Task Subscription_tenant_and_status_filters()
    {
        var platform = await _app.LoginAsync(TestUsers.PlatformAdmin, TestUsers.AdminPassword);
        var ofoq = await _app.WithDbAsync(db => db.Tenants.Where(t => t.Code == "OFOQ").Select(t => t.Id).FirstAsync());
        var subs = await (await platform.GetAsync($"/api/v1/subscriptions?tenantId={ofoq}&pageSize=100")).OkJsonAsync();
        Assert.NotEmpty(subs.GetProperty("items").EnumerateArray());
        Assert.All(subs.GetProperty("items").EnumerateArray(), s => Assert.Equal(ofoq, s.GetProperty("tenantId").GetGuid()));

        // A tenant user cannot widen its view with tenantId.
        var nour = await _app.LoginAsync(DemoAccounts.NourAdmin);
        var none = await (await nour.GetAsync($"/api/v1/subscriptions?tenantId={ofoq}&pageSize=100")).OkJsonAsync();
        Assert.All(none.GetProperty("items").EnumerateArray(), s => Assert.NotEqual(ofoq, s.GetProperty("tenantId").GetGuid()));
    }

    [Fact]
    public async Task Activation_state_filter()
    {
        var admin = await _app.LoginAsync(DemoAccounts.NourAdmin);
        foreach (var state in new[] { "online", "offline", "disabled" })
        {
            var list = await (await admin.GetAsync($"/api/v1/activations?state={state}&pageSize=100")).OkJsonAsync();
            foreach (var a in list.GetProperty("items").EnumerateArray())
            {
                if (state == "disabled") Assert.Equal("Deactivated", a.GetProperty("status").GetString());
                else Assert.Equal(state == "online", a.GetProperty("online").GetBoolean());
            }
        }
    }
}
