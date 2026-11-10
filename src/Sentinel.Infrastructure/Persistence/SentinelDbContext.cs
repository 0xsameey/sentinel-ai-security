
using Microsoft.EntityFrameworkCore;
using Sentinel.Domain.Entities;

namespace Sentinel.Infrastructure.Persistence;

public sealed class SentinelDbContext : DbContext
{
    public SentinelDbContext(
        DbContextOptions<SentinelDbContext> options)
        : base(options)
    {
    }

    public DbSet<AuditEvent> AuditEvents =>
        Set<AuditEvent>();

    public DbSet<AuthChallenge> AuthChallenges =>
    Set<AuthChallenge>();

    public DbSet<PaymentRequestRecord> PaymentRequests =>
    Set<PaymentRequestRecord>();

    public DbSet<PaymentTransactionAttempt> PaymentTransactionAttempts =>
        Set<PaymentTransactionAttempt>();
    public DbSet<SecurityAssessmentRecord> SecurityAssessments => Set<SecurityAssessmentRecord>();
    public DbSet<HumanApprovalRecord> HumanApprovals => Set<HumanApprovalRecord>();
    public DbSet<CounterpartyBlocklistEntry> CounterpartyBlocklist => Set<CounterpartyBlocklistEntry>();
    public DbSet<ExecutionPermitRecord> ExecutionPermits => Set<ExecutionPermitRecord>();

    public DbSet<DelegationRecord> Delegations => Set<DelegationRecord>();
    public DbSet<DelegatedPaymentRecord> DelegatedPayments => Set<DelegatedPaymentRecord>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<DelegationRecord>(e=> {
            e.ToTable("delegations"); e.HasKey(x=>new{x.ContractId,x.Id});
            e.Property(x=>x.Id).HasMaxLength(64);e.Property(x=>x.ContractId).HasMaxLength(64);
            e.Property(x=>x.StateJson).HasColumnType("jsonb");
        });
        modelBuilder.Entity<DelegatedPaymentRecord>(e=> {
            e.ToTable("delegated_payments");e.HasKey(x=>x.Id);e.Property(x=>x.Id).HasMaxLength(64);
            e.Property(x=>x.ContractId).HasMaxLength(64);e.Property(x=>x.DelegationId).HasMaxLength(64);
            e.Property(x=>x.IntentJson).HasColumnType("jsonb");e.Property(x=>x.DecisionJson).HasColumnType("jsonb");
            e.HasIndex(x=>x.PermitId).IsUnique();e.HasIndex(x=>x.TransactionHash).IsUnique();
            e.HasIndex(x=>new{x.ContractId,x.DelegationId});
        });
        modelBuilder.Entity<SecurityAssessmentRecord>(e => {
            e.ToTable("security_assessments"); e.HasKey(x => x.Id);
            e.Property(x => x.IntentJson).HasColumnType("jsonb"); e.Property(x => x.DecisionJson).HasColumnType("jsonb");
            e.Property(x => x.OwnerId).HasMaxLength(64); e.Property(x => x.ActorId).HasMaxLength(64);
            e.Property(x => x.ContractId).HasMaxLength(64); e.Property(x => x.PaymentId).HasMaxLength(64);
            e.Property(x => x.IntentHash).HasMaxLength(64); e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.Stage).HasMaxLength(16); e.Property(x => x.Decision).HasMaxLength(8);
            e.HasIndex(x => x.ExpiresAtUtc);
        });
        modelBuilder.Entity<HumanApprovalRecord>(e => {
            e.ToTable("human_approvals"); e.HasKey(x => x.Id); e.HasIndex(x => x.AssessmentId).IsUnique();
            e.HasOne<SecurityAssessmentRecord>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.OwnerId).HasMaxLength(64); e.Property(x => x.IntentHash).HasMaxLength(64);
            e.Property(x => x.Fingerprint).HasMaxLength(64); e.Property(x => x.Stage).HasMaxLength(16);
            e.Property(x => x.PaymentId).HasMaxLength(64); e.Property(x => x.ConsumedByPaymentId).HasMaxLength(64);
            e.HasIndex(x => new { x.PaymentId, x.ExpiresAtUtc });
        });
        modelBuilder.Entity<CounterpartyBlocklistEntry>(e => {
            e.ToTable("counterparty_blocklist"); e.HasKey(x => x.Id);
            e.Property(x => x.AccountId).HasMaxLength(64); e.Property(x => x.ReasonCode).HasMaxLength(64);
            e.Property(x => x.EvidenceUri).HasMaxLength(1024); e.Property(x => x.EvidenceSha256).HasMaxLength(64);
            e.Property(x => x.RecordedBy).HasMaxLength(64); e.HasIndex(x => x.AccountId);
        });
        modelBuilder.Entity<ExecutionPermitRecord>(e => {
            e.ToTable("execution_permits"); e.HasKey(x => x.PaymentId); e.Property(x => x.PaymentId).HasMaxLength(64);
            e.HasIndex(x => x.PermitId).IsUnique(); e.Property(x => x.Fingerprint).HasMaxLength(64);
            e.Property(x => x.IntentHash).HasMaxLength(64); e.Property(x => x.SignerPublicKey).HasMaxLength(128);
            e.HasOne<SecurityAssessmentRecord>().WithMany().HasForeignKey(x => x.AssessmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HumanApprovalRecord>().WithMany().HasForeignKey(x => x.ApprovalId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PaymentRequestRecord>(e => {
            e.Property(x => x.SecurityIntentHash).HasMaxLength(64);
            e.HasOne<SecurityAssessmentRecord>().WithMany().HasForeignKey(x => x.SecurityAssessmentId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HumanApprovalRecord>().WithMany().HasForeignKey(x => x.SecurityApprovalId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.ToTable("audit_events");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.ActorId)
                .HasMaxLength(128);

            entity.Property(x => x.PaymentId)
                .HasMaxLength(64);

            entity.Property(x => x.DetailsJson)
                .HasColumnType("jsonb");

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired()
                .HasColumnType("timestamp with time zone");

            entity.HasIndex(x => x.CreatedAtUtc);
            entity.HasIndex(x => x.PaymentId);
        });


        modelBuilder.Entity<AuthChallenge>(entity =>
        {
            entity.ToTable("auth_challenges");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.AccountId)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.NonceBase64)
                .IsRequired()
                .HasMaxLength(44);

            entity.Property(x => x.Message)
                .IsRequired()
                .HasMaxLength(512);

            entity.Property(x => x.Recipient)
                .IsRequired()
                .HasMaxLength(256);

            entity.Property(x => x.CreatedAtUtc)
                .IsRequired();

            entity.Property(x => x.ExpiresAtUtc)
                .IsRequired();

            entity.HasIndex(x => x.NonceBase64)
                .IsUnique();

            entity.HasIndex(x => x.ExpiresAtUtc);
        });


        modelBuilder.Entity<PaymentRequestRecord>(entity =>
        {
            entity.ToTable("payment_requests");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.PaymentId)
                .IsRequired()
                .HasMaxLength(64);

            entity.Property(x => x.IdempotencyKey)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.OwnerAccountId)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.ContractId)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.Recipient)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.AmountYocto)
                .IsRequired()
                .HasMaxLength(39);

            entity.Property(x => x.ExpiresAtNs)
                .HasConversion(
                    v => v.ToString(
                        System.Globalization.CultureInfo.InvariantCulture),
                    v => ulong.Parse(
                        v,
                        System.Globalization.CultureInfo.InvariantCulture))
                .HasMaxLength(20);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(32);

            entity.HasIndex(x => x.PaymentId).IsUnique();

            entity.HasIndex(x => new
            {
                x.ContractId,
                x.OwnerAccountId,
                x.IdempotencyKey
            }).IsUnique();


            entity.Property(x => x.Purpose)
                .IsRequired()
                .HasMaxLength(500)
                .HasDefaultValue("");

            entity.Property(x => x.RiskScore)
                .IsRequired()
                .HasDefaultValue(0);

            entity.Property(x => x.RiskLevel)
                .IsRequired()
                .HasMaxLength(16)
                .HasDefaultValue("");

            entity.Property(x => x.RiskDecision)
                .IsRequired()
                .HasMaxLength(16)
                .HasDefaultValue("");

            entity.Property(x => x.RiskReasonsJson)
                .IsRequired()
                .HasColumnType("jsonb")
                .HasDefaultValueSql("'[]'::jsonb");

            entity.Property(x => x.AiExplanation)
                .HasMaxLength(2000);


            entity.HasIndex(x => x.CreatedAtUtc);
        });

        modelBuilder.Entity<PaymentTransactionAttempt>(entity =>
        {
            entity.ToTable("payment_transaction_attempts");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.Operation)
                .IsRequired()
                .HasMaxLength(32);

            entity.Property(x => x.SignerAccountId)
                .IsRequired()
                .HasMaxLength(128);

            entity.Property(x => x.TransactionHash)
                .HasMaxLength(128);

            entity.Property(x => x.Status)
                .IsRequired()
                .HasMaxLength(32);

            entity.HasOne(x => x.PaymentRequest)
                .WithMany()
                .HasForeignKey(x => x.PaymentRequestId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(x => new
            {
                x.PaymentRequestId,
                x.Operation
            });

            entity.HasIndex(x => x.TransactionHash).IsUnique();
        });


    }
}

