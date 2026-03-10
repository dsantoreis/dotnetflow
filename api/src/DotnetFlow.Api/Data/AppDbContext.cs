using Microsoft.EntityFrameworkCore;
using DotnetFlow.Api.Models;

namespace DotnetFlow.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowTrigger> WorkflowTriggers => Set<WorkflowTrigger>();
    public DbSet<WorkflowExecution> WorkflowExecutions => Set<WorkflowExecution>();
    public DbSet<StepExecution> StepExecutions => Set<StepExecution>();
    public DbSet<Event> Events => Set<Event>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workflow>(e =>
        {
            e.HasKey(w => w.Id);
            e.HasMany(w => w.Steps).WithOne(s => s.Workflow).HasForeignKey(s => s.WorkflowId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(w => w.Triggers).WithOne(t => t.Workflow).HasForeignKey(t => t.WorkflowId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<WorkflowStep>(e =>
        {
            e.HasKey(s => s.Id);
        });

        modelBuilder.Entity<WorkflowTrigger>(e =>
        {
            e.HasKey(t => t.Id);
        });

        modelBuilder.Entity<WorkflowExecution>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Workflow).WithMany().HasForeignKey(x => x.WorkflowId);
            e.HasMany(x => x.StepExecutions).WithOne(s => s.WorkflowExecution).HasForeignKey(s => s.WorkflowExecutionId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<StepExecution>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.WorkflowStep).WithMany().HasForeignKey(s => s.WorkflowStepId);
        });

        modelBuilder.Entity<Event>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.HasIndex(ev => ev.Type);
            e.HasIndex(ev => ev.Processed);
        });
    }
}
