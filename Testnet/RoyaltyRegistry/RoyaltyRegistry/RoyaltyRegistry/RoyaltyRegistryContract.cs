using Stratis.SmartContracts;
using System;

/// <summary>
/// Defines a mapping of NFT tokens to royalty contracts. Able to be updated by the NFT collection's owner.
/// </summary>
public class RoyaltyRegistryContract : SmartContract, IRoyaltyRegistry
{
    public RoyaltyRegistryContract(ISmartContractState contractState) 
        : base(contractState)
    {
    }

    public Address GetOverride(Address address) => State.GetAddress($"Overrides:{address}");

    private void SetOverride(Address address, Address overrideAddress) => State.SetAddress($"Overrides:{address}", overrideAddress);

    public Address GetRoyaltyLookupAddress(Address tokenAddress)
    {
        var overrideAddress = GetOverride(tokenAddress);

        if (overrideAddress != Address.Zero) return overrideAddress;

        return tokenAddress;
    }

    private bool RoyaltyLookupIsContractOrEmpty(Address royaltyLookupAddress)
    {
        return State.IsContract(royaltyLookupAddress) && (State.IsContract(royaltyLookupAddress) || royaltyLookupAddress == Address.Zero);
    }

    public void SetRoyaltyLookupAddress(Address tokenAddress, Address royaltyLookupAddress)
    {
        Assert(State.IsContract(tokenAddress), "Lookup address must be a contract");
        Assert(RoyaltyLookupIsContractOrEmpty(royaltyLookupAddress), "Royalty address");
        Assert(OverrideAllowed(tokenAddress), "Override not allowed.");
        SetOverride(tokenAddress, royaltyLookupAddress);
        Log(new RoyaltyOverride { From = Message.Sender, TokenAddress = tokenAddress, RoyaltyLookupAddress = royaltyLookupAddress });
    }

    /// <summary>
    /// Determines whether the message sender can override the registry setting for the given token address.
    /// </summary>
    /// <param name="tokenAddress"></param>
    /// <returns></returns>
    public bool OverrideAllowed(Address tokenAddress)
    {
        // TODO determine the behaviour we want to allow here.
        // Are all NFTs collections? Should it be possible for the owner of an NFT collection to be able
        // to require royalties on sales that have already occurred?

        // Check that the NFT is owned by the address
        // TODO NFT interface doesn't define "Owner" property, is this guaranteed?
        var currentOwner = Call(tokenAddress, 0, "Owner");

        Assert(currentOwner.Success, "Couldn't retrieve owner.");

        return (Address) currentOwner.ReturnValue == Message.Sender;
    }

    public struct RoyaltyOverride
    {
        [Index]
        public Address From;

        [Index]
        public Address TokenAddress;

        [Index]
        public Address RoyaltyLookupAddress;
    }
}