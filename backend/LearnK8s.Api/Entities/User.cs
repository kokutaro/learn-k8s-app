using System.ComponentModel.DataAnnotations;

namespace LearnK8s.Api.Entities;

public class User
{
    public Guid Id { get; set; }

    [Required]
    [MaxLength(128)]
    public string Name { get; set; } = null!;

    [Required]
    [MaxLength(256)]
    public string Email { get; set; } = null!;
}