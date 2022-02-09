using Stratis.SmartContracts;

/// <summary>
/// Contract used to look up royalty configurations for a given token.
/// 
/// Checks if the token is registered in the registry, and if not, checks if it provides its own royalty information.
/// </summary>
public class RoyaltyLookup : SmartContract
{
    public RoyaltyLookup(ISmartContractState contractState, Address royaltyRegistry)
        : base(contractState)
    {
        RoyaltyRegistry = royaltyRegistry;
    }

    public Address RoyaltyRegistry
    {
        get => State.GetAddress(nameof(RoyaltyRegistry));
        set => State.SetAddress(nameof(RoyaltyRegistry), value);
    }

    /// <summary>
    /// Returns the address for the recipient of the royalties.
    /// </summary>
    /// <param name="tokenAddress"></param>
    /// <param name="tokenId"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public Address GetRoyaltyAddress(Address tokenAddress, UInt256 tokenId, UInt256 value)
    {
        // Get the address of the contract that contains the royalty payment info.
        var royaltyContractAddress = Call(RoyaltyRegistry, 0, nameof(RoyaltyRegistryContract.GetRoyaltyLookupAddress), new object[] { tokenAddress });

        Assert(royaltyContractAddress.Success, "Error getting royalty contract");

        // Attempt to call the 'Royalty Info' method on the returned contract.
        var royaltyInfo = Call((Address)royaltyContractAddress.ReturnValue, 0, "RoyaltyInfo", new object[] { tokenId, value });

        if (!royaltyInfo.Success)
        {
            // Possible no royalty info method was defined, default to no royalty information.
            return Address.Zero;
        }

        return (Address) royaltyInfo.ReturnValue;
    }
}