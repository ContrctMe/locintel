using System.Net;

namespace LocIntel.IntegrationTests;

public class SignupMethodTests(ApiFixture fixture) : IClassFixture<ApiFixture>
{
    [Fact]
    public async Task Signup_requires_explicit_valid_same_origin_post()
    {
        using var client = fixture.Factory.CreateDefaultClient();
        Assert.Equal(
            HttpStatusCode.MethodNotAllowed,
            (await client.GetAsync("/auth/signup?email=unrequested@example.test")).StatusCode
        );
        using var forged = new HttpRequestMessage(HttpMethod.Post, "/auth/signup")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { ["email"] = "forged@example.test" }
            ),
        };
        forged.Headers.Add("Origin", "https://evil.example.test");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(forged)).StatusCode);
        using var invalid = await client.PostAsync(
            "/auth/signup",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["email"] = "invalid" })
        );
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var valid = new HttpRequestMessage(HttpMethod.Post, "/auth/signup")
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string> { ["email"] = "New@Example.test" }
            ),
        };
        valid.Headers.Add("Origin", "http://localhost");
        using var response = await client.SendAsync(valid);
        Assert.Equal(HttpStatusCode.SeeOther, response.StatusCode);
        Assert.Equal(
            "/auth/login?hint=new%40example.test&signup=true",
            response.Headers.Location?.OriginalString
        );
    }
}
