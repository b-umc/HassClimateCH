#region Copyright

// ---------------------------------------------------------------------------
// Copyright © 2023 to the present, Crestron Electronics®, Inc.
// All Rights Reserved.
// No part of this software may be reproduced in any form, machine
// or natural, without the express written consent of Crestron Electronics.
// Use of this source code is subject to the terms of the Crestron Software
// License Agreement under which you licensed this source code.
// ---------------------------------------------------------------------------

#endregion

using Crestron.DeviceDrivers.SDK.EntityModel.Attributes;

namespace Definitions
{
    [EntityDataType(Id = "lightType:Variant")]
    public enum LightTypeVariant
    {
        /// <summary>
        /// A 'Load' means that the device controls a physical electrical circuit.
        /// By controlling a circuit, multiple physical lights (fixtures) can be controlled
        /// simultaneously (as many lights as are connected to the circuit)
        /// </summary>
        Load,

        /// <summary>
        /// A 'Fixture' means that the device controls a single physical light.
        /// Typically, the fixture will be connected to a circuit that is always powered on.
        /// </summary>
        Fixture,
    }
}