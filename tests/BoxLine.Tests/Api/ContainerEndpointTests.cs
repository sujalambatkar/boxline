using System.Net;
using System.Net.Http.Json;
using BoxLine.Api.Contracts;
using BoxLine.Core.Containers;
using BoxLine.Core.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;

namespace BoxLine.Tests.Api;

[Collection(ApiCollection.Name)]
public class ContainerEndpointTests(BoxLineApiFactory factory) : IAsyncLifetime
{
    private HttpClient client = null!;

    public async Task InitializeAsync() => client = await factory.CreateOpsClientAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SeededContainers_AllHaveValidIso6346Numbers()
    {
        var page = await GetPage("?pageSize=100&page=1");
        page.TotalCount.Should().BeGreaterThanOrEqualTo(200);

        var all = new List<string>();
        for (var number = 1; number <= 2; number++)
        {
            all.AddRange((await GetPage($"?pageSize=100&page={number}")).Items.Select(c => c.Number));
        }

        all.Should().HaveCount(200).And.OnlyHaveUniqueItems();
        all.Should().OnlyContain(n => Iso6346Validator.IsValid(n));
    }

    [Fact]
    public async Task List_PagesAndSortsOnTheServer()
    {
        var firstPage = await GetPage("?pageSize=5&page=1&sortBy=number&sortDirection=asc");
        var descending = await GetPage("?pageSize=5&page=1&sortBy=number&sortDirection=desc");

        firstPage.Items.Should().HaveCount(5);
        firstPage.Items.Select(c => c.Number).Should().BeInAscendingOrder();
        descending.Items.Select(c => c.Number).Should().BeInDescendingOrder();
        descending.Items[0].Number.Should().NotBe(firstPage.Items[0].Number);
    }

    [Fact]
    public async Task List_FiltersByStatusAndTypeAndSearch()
    {
        var available = await GetPage("?pageSize=100&status=Available");
        available.Items.Should().NotBeEmpty().And.OnlyContain(c => c.Status == ContainerStatus.Available);

        var sample = available.Items[0];
        var byType = await GetPage($"?pageSize=100&containerTypeId={sample.ContainerTypeId}");
        byType.Items.Should().OnlyContain(c => c.ContainerTypeId == sample.ContainerTypeId);

        var bySearch = await GetPage($"?search={sample.Number[..4].ToLowerInvariant()}%20{sample.Number[4..8]}");
        bySearch.Items.Should().Contain(c => c.Number == sample.Number);
    }

    [Fact]
    public async Task List_RejectsUnknownSortField()
    {
        var response = await client.GetAsync("/api/containers?sortBy=colour");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_RejectsAnInvalidContainerNumberWithAClearMessage()
    {
        var typeId = await AnyTypeId();

        var response = await client.PostAsJsonAsync("/api/containers",            new CreateContainerRequest("CSQU3054380", typeId, OwnershipType.Owned));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!;
        problem.Errors["Number"].Single().Should().Contain("Check digit is wrong").And.Contain("should end in 3");
    }

    [Fact]
    public async Task Create_NormalizesTheNumber_AndRejectsDuplicates_ThenCleansUp()
    {
        var typeId = await AnyTypeId();
        var number = NewUnusedNumber();

        var create = await client.PostAsJsonAsync("/api/containers",            new CreateContainerRequest(number.ToLowerInvariant(), typeId, OwnershipType.Leased));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = (await create.Content.ReadFromJsonAsync<ContainerResponse>(TestJson.Options))!;
        created.Number.Should().Be(number);
        created.Status.Should().Be(ContainerStatus.Available);

        (await client.PostAsJsonAsync("/api/containers", new CreateContainerRequest(number, typeId, OwnershipType.Owned)))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var update = await client.PutAsJsonAsync($"/api/containers/{created.Id}",
            new UpdateContainerRequest(typeId, OwnershipType.Owned, ContainerStatus.UnderRepair));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await update.Content.ReadFromJsonAsync<ContainerResponse>(TestJson.Options))!;
        updated.OwnershipType.Should().Be(OwnershipType.Owned);
        updated.Status.Should().Be(ContainerStatus.UnderRepair);
        updated.Number.Should().Be(number);

        (await client.DeleteAsync($"/api/containers/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/containers/{created.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Create_RejectsAnUnknownContainerType()
    {
        var response = await client.PostAsJsonAsync("/api/containers",            new CreateContainerRequest(NewUnusedNumber(), 999_999, OwnershipType.Owned));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<PagedResponse<ContainerResponse>> GetPage(string query) =>
        (await client.GetFromJsonAsync<PagedResponse<ContainerResponse>>($"/api/containers{query}", TestJson.Options))!;

    private async Task<int> AnyTypeId() =>
        (await client.GetFromJsonAsync<List<ContainerTypeResponse>>("/api/container-types"))![0].Id;

    /// <summary>A valid number under a prefix the seeder never uses, so it cannot collide with sample data.</summary>
    private static string NewUnusedNumber()
    {
        var body = $"TSTU{Random.Shared.Next(0, 1_000_000):D6}";
        return body + Iso6346Validator.ComputeCheckDigit(body);
    }
}
