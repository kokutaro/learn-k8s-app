namespace LearnK8s.Api.Requests;

public record struct CreateItemRequest(
    string Name,
    string? Description,
    decimal Price
);