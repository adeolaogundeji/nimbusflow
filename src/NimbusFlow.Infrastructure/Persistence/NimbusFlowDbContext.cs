using Microsoft.EntityFrameworkCore;
using NimbusFlow.Domain.Jobs;
using NimbusFlow.Domain.Messaging;

namespace NimbusFlow.Infrastructure.Persistence;

public sealed class NimbusFlowDbContext(DbContextOptions<NimbusFlowDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowJob> Jobs => Set<WorkflowJob>();

    public DbSet<JobEvent> JobEvents => Set<JobEvent>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<WorkflowJob>(entity =>
        {
            entity.ToTable("WorkflowJobs");
            entity.HasKey(job => job.Id);
            entity.Property(job => job.WorkflowType).HasMaxLength(100).IsRequired();
            entity.Property(job => job.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(job => job.IdempotencyKey).HasMaxLength(128);
            entity.Property(job => job.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(job => job.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(job => job.CurrentStep).HasMaxLength(200).IsRequired();
            entity.Property(job => job.ResultJson).HasColumnType("nvarchar(max)");
            entity.Property(job => job.ErrorCode).HasMaxLength(100);
            entity.Property(job => job.ErrorMessage).HasMaxLength(2_000);
            entity.Property(job => job.RowVersion).IsRowVersion();
            entity.HasIndex(job => job.IdempotencyKey)
                .IsUnique()
                .HasFilter("[IdempotencyKey] IS NOT NULL");
            entity.HasIndex(job => new { job.Status, job.CreatedAt });
            entity.HasIndex(job => new { job.WorkflowType, job.CreatedAt });
            entity.HasMany(job => job.Events)
                .WithOne(jobEvent => jobEvent.Job)
                .HasForeignKey(jobEvent => jobEvent.JobId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<JobEvent>(entity =>
        {
            entity.ToTable("JobEvents");
            entity.HasKey(jobEvent => jobEvent.Id);
            entity.Property(jobEvent => jobEvent.Id).ValueGeneratedOnAdd();
            entity.Property(jobEvent => jobEvent.EventType).HasMaxLength(100).IsRequired();
            entity.Property(jobEvent => jobEvent.Message).HasMaxLength(2_000).IsRequired();
            entity.HasIndex(jobEvent => new { jobEvent.JobId, jobEvent.OccurredAt });
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("OutboxMessages");
            entity.HasKey(message => message.Id);
            entity.Property(message => message.MessageType).HasMaxLength(200).IsRequired();
            entity.Property(message => message.PayloadJson).HasColumnType("nvarchar(max)").IsRequired();
            entity.Property(message => message.CorrelationId).HasMaxLength(128).IsRequired();
            entity.Property(message => message.LastError).HasMaxLength(2_000);
            entity.Property(message => message.RowVersion).IsRowVersion();
            entity.HasIndex(message => new { message.ProcessedAt, message.AvailableAt });
        });
    }
}
