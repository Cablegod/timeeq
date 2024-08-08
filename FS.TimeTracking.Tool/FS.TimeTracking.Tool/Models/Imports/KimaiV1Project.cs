﻿using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FS.TimeTracking.Tool.Models.Imports;

/// <summary>
/// Project
/// </summary>
public class KimaiV1Project
{
    /// <summary>
    /// Gets the unique identifier for <see cref="Project.Id"/>.
    /// </summary>
    /// <autogeneratedoc />
    [NotMapped]
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Gets the unique identifier.
    /// </summary>
    [Required]
    public int ProjectId { get; set; }

    /// <summary>
    /// Gets the short/display name.
    /// </summary>
    [Required]
    [StringLength(100)]
    public string Name { get; set; }

    /// <summary>
    /// Gets or sets a comment.
    /// </summary>
    public string Comment { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is hidden.
    /// </summary>
    [Required]
    public bool Visible { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether this item is hidden.
    /// </summary>
    [Required]
    public bool Trash { get; set; }

    /// <summary>
    /// Gets or sets the identifier to the related <see cref="Customer"/>.
    /// </summary>
    [Required]
    public int CustomerId { get; set; }

    /// <inheritdoc cref="KimaiV1Customer"/>
    public KimaiV1Customer Customer { get; set; }
}