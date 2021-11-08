﻿using Stratis.SmartContracts;
using System;

[Deploy]
public class DAOContract : SmartContract
{
    public Address Owner
    {
        get => State.GetAddress(nameof(Owner));
        private set => State.SetAddress(nameof(Owner), value);
    }

    public Address PendingOwner
    {
        get => State.GetAddress(nameof(PendingOwner));
        private set => State.SetAddress(nameof(PendingOwner), value);
    }

    public uint MinQuorum => WhitelistedCount / 2 + 1;

    public uint WhitelistedCount
    {
        get => State.GetUInt32(nameof(WhitelistedCount));
        private set => State.SetUInt32(nameof(WhitelistedCount), value);
    }

    public uint MinVotingDuration
    {
        get => State.GetUInt32(nameof(MinVotingDuration));
        private set => State.SetUInt32(nameof(MinVotingDuration), value);
    }

    public uint MaxVotingDuration
    {
        get => State.GetUInt32(nameof(MaxVotingDuration));
        private set => State.SetUInt32(nameof(MaxVotingDuration), value);
    }

    public uint LastProposalId
    {
        get => State.GetUInt32(nameof(LastProposalId));
        private set => State.SetUInt32(nameof(LastProposalId), value);
    }

    public bool IsWhitelisted(Address address) => State.GetBool($"Whitelisted:{address}");
    private void SetIsWhitelisted(Address address, bool allowed) => State.SetBool($"Whitelisted:{address}", allowed);

    public ulong GetVotingDeadline(uint proposalId) => State.GetUInt64($"Deadline:{proposalId}");
    private void SetVotingDeadline(uint proposalId, ulong block) => State.SetUInt64($"Deadline:{proposalId}", block);

    public uint GetVotes(uint proposalId, bool vote) => State.GetUInt32($"Votes:{proposalId}:{vote}");
    private void SetVotes(uint proposalId, bool vote, uint value) => State.SetUInt32($"Votes:{proposalId}:{vote}", value);

    public uint GetYesVotes(uint proposalId) => GetVotes(proposalId, true);

    public uint GetNoVotes(uint proposalId) => GetVotes(proposalId, false);

    public uint GetVote(uint proposalId, Address address) => State.GetUInt32($"Vote:{proposalId}:{address}");
    private void SetVote(uint proposalId, Address address, Votes vote) => State.SetUInt32($"Vote:{proposalId}:{address}", (uint)vote);

    public Proposal GetProposal(uint index) => State.GetStruct<Proposal>($"Proposals:{index}");

    private void SetProposal(uint index, Proposal proposal) => State.SetStruct($"Proposals:{index}", proposal);

    public DAOContract(ISmartContractState state, uint minVotingDuration, uint maxVotingDuration)
        : base(state)
    {
        Assert(maxVotingDuration > minVotingDuration, $"MinVotingDuration should be lower than maxVotingDuration({maxVotingDuration})");

        Owner = Message.Sender;
        LastProposalId = 1;
        MinVotingDuration = minVotingDuration;
        MaxVotingDuration = maxVotingDuration;
    }

    public uint CreateProposal(Address recipient, ulong amount, uint votingDuration, string description)
    {
        EnsureNotPayable();

        Assert(votingDuration > MinVotingDuration && votingDuration < MaxVotingDuration, $"Voting duration should be between {MinVotingDuration} and {MaxVotingDuration}.");

        var length = description?.Length ?? 0;
        Assert(length <= 200, "The description length can be up to 200 characters.");

        var proposal = new Proposal
        {
            RequestedAmount = amount,
            Description = description,
            Recipient = recipient,
            Owner = Message.Sender
        };

        var proposalId = LastProposalId;
        SetProposal(proposalId, proposal);
        SetVotingDeadline(proposalId, checked(votingDuration + Block.Number));
        Log(new ProposalAddedLog
        {
            ProposalId = proposalId,
            Recipient = recipient,
            Amount = amount,
            Description = description
        });

        LastProposalId = proposalId + 1;

        return proposalId;
    }

    public void Vote(uint proposalId, bool vote)
    {
        EnsureNotPayable();
        Assert(IsWhitelisted(Message.Sender), "The caller is not whitelisted.");

        Assert(GetVotingDeadline(proposalId) > Block.Number, "Voting is closed.");

        VoteProposal(proposalId, vote);
    }

    private void VoteProposal(uint proposalId, bool vote)
    {
        var currentVote = (Votes)GetVote(proposalId, Message.Sender);

        var voteEnum = ToVoteEnum(vote);
        if (currentVote == voteEnum)
        {
            return;
        }

        Unvote(proposalId, currentVote);

        SetVote(proposalId, Message.Sender, voteEnum);

        Log(new ProposalVotedLog { ProposalId = proposalId, Voter = Message.Sender, Vote = vote });

        var voteCount = GetVotes(proposalId, vote);

        SetVotes(proposalId, vote, voteCount + 1);
    }

    private void Unvote(uint proposalId, Votes vote)
    {
        if (vote == Votes.None)
            return;

        var voteBool = ToVoteBool(vote);
        var voteCount = GetVotes(proposalId, voteBool);

        SetVotes(proposalId, voteBool, voteCount - 1);
    }

    private Votes ToVoteEnum(bool vote) => vote ? Votes.Yes : Votes.No;
    private bool ToVoteBool(Votes vote) => vote == Votes.Yes;

    public void ExecuteProposal(uint proposalId)
    {
        EnsureNotPayable();

        var proposal = GetProposal(proposalId);

        var yesVotes = GetYesVotes(proposalId);
        var noVotes = GetNoVotes(proposalId);

        Assert(yesVotes > noVotes, "The proposal voting is not passed.");

        Assert(yesVotes >= MinQuorum, "Min quorum for proposal is not reached.");

        Assert(!proposal.Executed, "The proposal is already executed.");
        Assert(GetVotingDeadline(proposalId) < Block.Number, "Voting is still open for the proposal.");
        Assert(proposal.RequestedAmount <= Balance, "Insufficient balance.");

        proposal.Executed = true;
        SetProposal(proposalId, proposal);
        var result = Transfer(proposal.Recipient, proposal.RequestedAmount);

        Assert(result.Success, "Transfer failed.");

        Log(new ProposalExecutedLog { ProposalId = proposalId, Amount = proposal.RequestedAmount, Recipient = proposal.Recipient });

    }

    public void BlacklistAddress(Address address)
    {
        EnsureOwnerOnly();

        if (!IsWhitelisted(address))
            return;

        SetIsWhitelisted(address, false);
        WhitelistedCount--;
    }

    public void BlacklistAddresses(byte[] addresses)
    {
        EnsureOwnerOnly();
        foreach (var address in Serializer.ToArray<Address>(addresses))
        {
            if (!IsWhitelisted(address))
                continue;

            SetIsWhitelisted(address, false);
            WhitelistedCount--;
        }
    }

    public void WhitelistAddress(Address address)
    {
        EnsureOwnerOnly();

        if (IsWhitelisted(address))
            return;

        SetIsWhitelisted(address, true);

        WhitelistedCount++;
    }

    public void WhitelistAddresses(byte[] addresses)
    {
        EnsureOwnerOnly();
        foreach (var address in Serializer.ToArray<Address>(addresses))
        {
            if (IsWhitelisted(address))
                continue;

            SetIsWhitelisted(address, true);
            WhitelistedCount++;
        }
    }

    public void Deposit()
    {
        Log(new FundRaisedLog { Sender = Message.Sender, Amount = Message.Value });
    }

    public override void Receive() => Deposit();

    public void SetPendingOwner(Address newOwner)
    {
        EnsureOwnerOnly();
        PendingOwner = newOwner;
    }

    public void ClaimOwnership()
    {
        var newOwner = PendingOwner;

        Assert(newOwner == Message.Sender, "ClaimOwnership must be called by the new(pending) owner.");

        var oldOwner = Owner;
        Owner = newOwner;
        PendingOwner = Address.Zero;

        Log(new OwnerTransferredLog { From = oldOwner, To = newOwner });
    }

    private void EnsureOwnerOnly() => Assert(this.Owner == Message.Sender, "The method is owner only.");
    private void EnsureNotPayable() => Assert(Message.Value == 0, "The method is not payable.");

    public enum Votes : uint
    {
        None,
        No,
        Yes
    }

    public struct ProposalAddedLog
    {
        [Index]
        public Address Recipient;
        [Index]
        public uint ProposalId;
        public ulong Amount;
        public string Description;
    }
    public struct ProposalExecutedLog
    {
        [Index]
        public Address Recipient;
        [Index]
        public uint ProposalId;
        public ulong Amount;
    }
    public struct FundRaisedLog
    {
        [Index]
        public Address Sender;
        public ulong Amount;
    }
    public struct ProposalVotedLog
    {
        [Index]
        public uint ProposalId;
        [Index]
        public Address Voter;
        public bool Vote;
    }

    public struct Proposal
    {
        public Address Owner;

        public Address Recipient;

        public ulong RequestedAmount;

        public string Description;

        /// <summary>
        /// True if proposal executed and the requested fund is transferred
        /// </summary>
        public bool Executed;
    }
    public struct OwnerTransferredLog
    {
        [Index]
        public Address From;
        [Index]
        public Address To;
    }
}

