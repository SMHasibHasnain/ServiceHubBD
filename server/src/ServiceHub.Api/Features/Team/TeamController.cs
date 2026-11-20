using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Access;
using ServiceHub.Api.Common;
using ServiceHub.Api.Data;
using ServiceHub.Api.Domain;
using ServiceHub.Api.Domain.Entities;
using ServiceHub.Api.Features.Billing;

namespace ServiceHub.Api.Features.Team;

public record RoleDto(short Id, string Key, RoleScope Scope, string NameBn, string NameEn, short Rank, string[] Permissions);

public record MemberDto(Guid Id, Guid UserId, string FullName, string Phone, string RoleKey, string RoleName, string? Title,
    MemberStatus Status, DateTime JoinedAt);

public record InviteRequest([Required] string Phone, [Required] string RoleKey, [StringLength(80)] string? Title);

public record InvitationDto(Guid Id, Guid BusinessId, string BusinessName, string? InviteePhone, string RoleKey, string RoleName,
    InvitationStatus Status, DateTime ExpiresAt, DateTime CreatedAt);

/// <summary>The raw token is returned once so the client can build the invite link; only its hash is stored.</summary>
public record InvitationCreatedDto(InvitationDto Invitation, string Token);

public record AcceptInvitationRequest([Required] string Token);

public record UpdateMemberRequest(string? RoleKey, [StringLength(80)] string? Title, MemberStatus? Status);

[ApiController]
[Authorize]
[Route("api")]
public class TeamController(AppDbContext db, ICurrentUser me, AccessService access, AuditLogger audit, Notifier notifier,
    ISmsSender sms, EntitlementService plans) : ControllerBase
{
    private const int InvitationDays = 7;

    [HttpGet("roles")]
    public async Task<ActionResult<List<RoleDto>>> Roles([FromQuery] RoleScope? scope) =>
        await db.Roles.Where(r => r.BusinessId == null && (scope == null || r.Scope == scope))
            .OrderBy(r => r.Scope).ThenByDescending(r => r.Rank)
            .Select(r => new RoleDto(r.Id, r.Key, r.Scope, r.NameBn, r.NameEn, r.Rank,
                r.RolePermissions.Select(rp => rp.Permission.Key).OrderBy(k => k).ToArray()))
            .ToListAsync();

    [HttpGet("businesses/{businessId:guid}/members")]
    public async Task<ActionResult<List<MemberDto>>> Members(Guid businessId)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.MembersView);
        return await db.BusinessMembers.Where(m => m.BusinessId == businessId && m.Status != MemberStatus.Removed)
            .OrderByDescending(m => m.Role.Rank).ThenBy(m => m.User.FullName)
            .Select(m => new MemberDto(m.Id, m.UserId, m.User.FullName, m.User.Phone, m.Role.Key, m.Role.NameBn, m.Title, m.Status, m.JoinedAt))
            .ToListAsync();
    }

    [HttpGet("businesses/{businessId:guid}/invitations")]
    public async Task<ActionResult<List<InvitationDto>>> Invitations(Guid businessId)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.MembersView);
        return await InvitationQuery(db.BusinessInvitations.Where(i => i.BusinessId == businessId && i.Status == InvitationStatus.Pending))
            .ToListAsync();
    }

    /// <summary>Invites a person by phone — they may not have an account yet.</summary>
    [HttpPost("businesses/{businessId:guid}/invitations")]
    public async Task<ActionResult<InvitationCreatedDto>> Invite(Guid businessId, InviteRequest req)
    {
        var actor = await access.RequireBusinessOrStaffAsync(businessId, P.MembersManage);
        var role = await AssignableRoleAsync(req.RoleKey, actor);
        var phone = Phone.NormalizeBd(req.Phone) ?? throw new BadRequestException("সঠিক মোবাইল নম্বর লিখুন।", "invalid_phone");

        var invitee = await db.Users.FirstOrDefaultAsync(u => u.Phone == phone);
        if (invitee is not null && await db.BusinessMembers.AnyAsync(m => m.BusinessId == businessId && m.UserId == invitee.Id && m.Status != MemberStatus.Removed))
            throw new ConflictException("এই ব্যক্তি ইতিমধ্যে টিমে আছেন।", "already_member");
        // Re-inviting the same phone replaces the old invitation, so it doesn't count twice.
        await plans.RequireCapacityAsync(businessId, E.MembersMax, await plans.TeamSizeAsync(businessId, exceptInvitePhone: phone));
        await db.BusinessInvitations.Where(i => i.BusinessId == businessId && i.InviteePhone == phone && i.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Revoked));

        var token = Tokens.NewToken();
        var invitation = new BusinessInvitation
        {
            BusinessId = businessId, RoleId = role.Id, InviteePhone = phone, InviteeId = invitee?.Id,
            TokenHash = Tokens.Sha256(token), InvitedBy = me.RequireId(), ExpiresAt = DateTime.UtcNow.AddDays(InvitationDays),
        };
        db.BusinessInvitations.Add(invitation);
        var businessName = await db.Businesses.Where(b => b.Id == businessId).Select(b => b.Name).FirstAsync();
        if (invitee is not null)
            notifier.Notify(invitee.Id, WorkspaceType.Customer, null, "team.invited",
                $"“{businessName}” আপনাকে {role.NameBn} হিসেবে যোগ দিতে আমন্ত্রণ জানিয়েছে।",
                data: new() { ["invitationId"] = invitation.Id.ToString() });
        audit.Log("member.invite", "business_invitation", invitation.Id, businessId: businessId, scope: actor.Scope,
            roleId: actor.RoleId, changes: new() { ["phone"] = phone, ["role"] = role.Key });
        await db.SaveChangesAsync();
        await sms.SendAsync(phone, $"“{businessName}” আপনাকে ServiceHub BD-তে টিম সদস্য হিসেবে আমন্ত্রণ জানিয়েছে। কোড: {token}");

        var dto = await InvitationQuery(db.BusinessInvitations.Where(i => i.Id == invitation.Id)).FirstAsync();
        return new InvitationCreatedDto(dto, token);
    }

    [HttpDelete("businesses/{businessId:guid}/invitations/{invitationId:guid}")]
    public async Task<IActionResult> Revoke(Guid businessId, Guid invitationId)
    {
        await access.RequireBusinessOrStaffAsync(businessId, P.MembersManage);
        await db.BusinessInvitations.Where(i => i.Id == invitationId && i.BusinessId == businessId && i.Status == InvitationStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.Status, InvitationStatus.Revoked));
        return NoContent();
    }

    /// <summary>Pending invitations addressed to my phone number.</summary>
    [HttpGet("me/invitations")]
    public async Task<ActionResult<List<InvitationDto>>> MyInvitations()
    {
        var phone = await db.Users.Where(u => u.Id == me.RequireId()).Select(u => u.Phone).FirstAsync();
        return await InvitationQuery(db.BusinessInvitations.Where(i =>
            i.InviteePhone == phone && i.Status == InvitationStatus.Pending && i.ExpiresAt > DateTime.UtcNow)).ToListAsync();
    }

    [HttpPost("invitations/accept")]
    public async Task<ActionResult<MemberDto>> Accept(AcceptInvitationRequest req)
    {
        var userId = me.RequireId();
        var hash = Tokens.Sha256(req.Token.Trim());
        var inv = await db.BusinessInvitations.Include(i => i.Role).FirstOrDefaultAsync(i => i.TokenHash == hash)
                  ?? throw new NotFoundException("আমন্ত্রণটি পাওয়া যায়নি।", "invitation_not_found");
        var user = await db.Users.FirstAsync(u => u.Id == userId);
        if (inv.Status != InvitationStatus.Pending || inv.ExpiresAt <= DateTime.UtcNow)
            throw new ConflictException("আমন্ত্রণটির মেয়াদ শেষ অথবা বাতিল হয়েছে।", "invitation_expired");
        if (inv.InviteePhone != user.Phone)
            throw new ForbiddenException("এই আমন্ত্রণটি অন্য মোবাইল নম্বরের জন্য।", "invitation_mismatch");

        var member = await db.BusinessMembers.FirstOrDefaultAsync(m => m.BusinessId == inv.BusinessId && m.UserId == userId);
        if (member is not null && member.Status != MemberStatus.Removed)
            throw new ConflictException("আপনি ইতিমধ্যে এই টিমে আছেন।", "already_member");
        if (member is null)
        {
            member = new BusinessMember { BusinessId = inv.BusinessId, UserId = userId };
            db.BusinessMembers.Add(member);
        }
        member.RoleId = inv.RoleId;
        member.Status = MemberStatus.Active;
        member.InvitedBy = inv.InvitedBy;
        member.JoinedAt = DateTime.UtcNow;
        member.RemovedAt = null;
        member.RemovedBy = null;
        inv.Status = InvitationStatus.Accepted;
        inv.RespondedAt = DateTime.UtcNow;
        inv.InviteeId = userId;
        await notifier.NotifyBusinessAsync(inv.BusinessId, P.MembersManage, "team.joined", $"{user.FullName} টিমে যোগ দিয়েছেন।");
        await db.SaveChangesAsync();
        return new MemberDto(member.Id, userId, user.FullName, user.Phone, inv.Role.Key, inv.Role.NameBn, member.Title, member.Status, member.JoinedAt);
    }

    [HttpPatch("businesses/{businessId:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<MemberDto>> Update(Guid businessId, Guid memberId, UpdateMemberRequest req)
    {
        var actor = await access.RequireBusinessOrStaffAsync(businessId, P.MembersManage);
        var member = await LoadMemberAsync(businessId, memberId);
        EnsureCanManage(actor, member);
        if (req.RoleKey is not null)
        {
            var role = await AssignableRoleAsync(req.RoleKey, actor);
            audit.Log("member.role_change", "business_member", member.Id, businessId: businessId, scope: actor.Scope,
                roleId: actor.RoleId, changes: new() { ["from"] = member.Role.Key, ["to"] = role.Key });
            member.RoleId = role.Id;
            member.Role = role;
        }
        if (req.Title is not null) member.Title = req.Title;
        if (req.Status is MemberStatus.Active or MemberStatus.Suspended)
        {
            audit.Log("member.status_change", "business_member", member.Id, businessId: businessId, scope: actor.Scope,
                roleId: actor.RoleId, changes: new() { ["to"] = SnakeCase.FromEnum(req.Status.Value) });
            member.Status = req.Status.Value;
        }
        await db.SaveChangesAsync();
        return new MemberDto(member.Id, member.UserId, member.User.FullName, member.User.Phone, member.Role.Key, member.Role.NameBn,
            member.Title, member.Status, member.JoinedAt);
    }

    /// <summary>Removes a member (or lets a member leave). The owner can't be removed.</summary>
    [HttpDelete("businesses/{businessId:guid}/members/{memberId:guid}")]
    public async Task<IActionResult> Remove(Guid businessId, Guid memberId)
    {
        var userId = me.RequireId();
        var member = await LoadMemberAsync(businessId, memberId);
        if (member.Role.Key == R.BusinessOwner)
            throw new ConflictException("মালিককে টিম থেকে সরানো যায় না।", "cannot_remove_owner");
        short? actorRoleId = member.RoleId;
        var actorScope = RoleScope.Business;
        if (member.UserId != userId)
        {
            var actor = await access.RequireBusinessOrStaffAsync(businessId, P.MembersManage);
            EnsureCanManage(actor, member);
            (actorRoleId, actorScope) = (actor.RoleId, actor.Scope);
        }
        member.Status = MemberStatus.Removed;
        member.RemovedAt = DateTime.UtcNow;
        member.RemovedBy = userId;
        await db.Bookings.Where(b => b.AssignedMemberId == member.Id && b.StartAt > DateTime.UtcNow)
            .ExecuteUpdateAsync(s => s.SetProperty(b => b.AssignedMemberId, (Guid?)null));
        audit.Log("member.remove", "business_member", member.Id, businessId: businessId, scope: actorScope, roleId: actorRoleId);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private static void EnsureCanManage(BusinessActor actor, BusinessMember target)
    {
        if (target.Role.Rank >= actor.Rank)
            throw new ForbiddenException("নিজের সমান বা উচ্চতর ভূমিকার সদস্যকে পরিবর্তন করা যায় না।", "insufficient_rank");
    }

    /// <summary>Business roles strictly below the actor's rank. Ownership is transferred, never assigned.</summary>
    private async Task<Role> AssignableRoleAsync(string key, BusinessActor actor)
    {
        var role = await db.Roles.FirstOrDefaultAsync(r => r.Key == key && r.Scope == RoleScope.Business &&
                                                           (r.BusinessId == null || r.BusinessId == actor.BusinessId))
                   ?? throw new BadRequestException("ভূমিকাটি পাওয়া যায়নি।", "invalid_role");
        if (role.Key == R.BusinessOwner || role.Rank >= actor.Rank)
            throw new ForbiddenException("এই ভূমিকা দেওয়ার অনুমতি আপনার নেই।", "insufficient_rank");
        return role;
    }

    private async Task<BusinessMember> LoadMemberAsync(Guid businessId, Guid memberId) =>
        await db.BusinessMembers.Include(m => m.Role).Include(m => m.User)
            .FirstOrDefaultAsync(m => m.Id == memberId && m.BusinessId == businessId && m.Status != MemberStatus.Removed)
        ?? throw new NotFoundException("সদস্য পাওয়া যায়নি।", "member_not_found");

    private static IQueryable<InvitationDto> InvitationQuery(IQueryable<BusinessInvitation> q) =>
        q.OrderByDescending(i => i.CreatedAt).Select(i => new InvitationDto(i.Id, i.BusinessId, i.Business.Name, i.InviteePhone,
            i.Role.Key, i.Role.NameBn, i.Status, i.ExpiresAt, i.CreatedAt));
}
