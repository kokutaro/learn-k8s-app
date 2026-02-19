using System.ComponentModel.DataAnnotations;

namespace LearnK8s.Api.Entities;

public class Item
{
    public Guid Id { get; set; }

    [Required] [MaxLength(128)] 
    public string Name { get; set; } = null!;

    [MaxLength(256)] 
    public string? Description { get; set; }

    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
    public string? LastUpdatedBy { get; set; }
}