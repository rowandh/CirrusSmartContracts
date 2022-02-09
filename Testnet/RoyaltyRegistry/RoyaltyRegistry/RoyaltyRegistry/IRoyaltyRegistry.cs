using Stratis.SmartContracts;
using System;
using System.Collections.Generic;
using System.Text;

public interface IRoyaltyRegistry
{
    void SetRoyaltyLookupAddress(Address tokenAddress, Address royaltyAddress);

    Address GetRoyaltyLookupAddress(Address tokenAddress);

    bool OverrideAllowed(Address tokenAddress);
}