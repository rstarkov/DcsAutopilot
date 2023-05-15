local socket = require("socket")
local lfs = require('lfs')
local UdpSocket = nil
local LogFile = nil
local CurFrame = 0
local Skips = 0
local Session = socket.gettime()
local Latency = 0
local LastBulkData = 0
local AutoResetPCA = { }



function LuaExportStart()
    LogFile = io.open(lfs.writedir().."Logs/DcsAutopilot.log", "w")
    LogFile:write("DcsAutopilot script started\n\n")
    LogDiagnostics()

    UdpSocket = socket.udp()
    UdpSocket:settimeout(0)
    
    SendBulkData()
end



function LuaExportStop()
    LogFile:close()
end



function LuaExportAfterNextFrame()
    local dt = { }
    -- table.insert is very slow even for pure appends, because Lua. #dt+1 is noticeably slower than
    -- a manual counter, also because Lua. We can't inline increment either, so the optimal performance
    -- option is to insert a counter increment after each line. That's too cumbersome. Isn't Lua amazing.
    dt[#dt+1] = "frame"
    dt[#dt+1] = "sess"
    dt[#dt+1] = Session
    dt[#dt+1] = "skips"
    dt[#dt+1] = Skips
    dt[#dt+1] = "time"
    dt[#dt+1] = LoGetModelTime()
    dt[#dt+1] = "sent"
    dt[#dt+1] = socket.gettime()
    dt[#dt+1] = "ltcy"
    dt[#dt+1] = Latency
    local ownshipExport = LoIsOwnshipExportAllowed()
    dt[#dt+1] = "exp"
    dt[#dt+1] = ownshipExport and "true" or "false"
    if ownshipExport then
        local sdata = LoGetSelfData()
        local engine = LoGetEngineInfo()
        local mech = LoGetMechInfo()
        
        dt[#dt+1] = "pitch"
        dt[#dt+1] = sdata.Pitch
        dt[#dt+1] = "bank"
        dt[#dt+1] = sdata.Bank
        dt[#dt+1] = "hdg"
        dt[#dt+1] = sdata.Heading
        dt[#dt+1] = "ang"
        local ang = LoGetAngularVelocity()
        dt[#dt+1] = ang.x
        dt[#dt+1] = ang.y
        dt[#dt+1] = ang.z
        dt[#dt+1] = "pos" -- cheaty
        dt[#dt+1] = sdata.Position.x
        dt[#dt+1] = sdata.Position.y
        dt[#dt+1] = sdata.Position.z
        dt[#dt+1] = "vel" -- cheaty
        local vel = LoGetVectorVelocity()
        dt[#dt+1] = vel.x
        dt[#dt+1] = vel.y
        dt[#dt+1] = vel.z
        dt[#dt+1] = "acc"
        local acc = LoGetAccelerationUnits()
        dt[#dt+1] = acc.x
        dt[#dt+1] = acc.y
        dt[#dt+1] = acc.z
        dt[#dt+1] = "asl" -- cheaty?
        dt[#dt+1] = LoGetAltitudeAboveSeaLevel()
        dt[#dt+1] = "agl" -- cheaty?
        dt[#dt+1] = LoGetAltitudeAboveGroundLevel()
        dt[#dt+1] = "balt" -- this is just nil on the Hornet
        dt[#dt+1] = LoGetAltitude() or 0
        dt[#dt+1] = "ralt"
        dt[#dt+1] = LoGetRadarAltimeter()
        dt[#dt+1] = "vspd"
        dt[#dt+1] = LoGetVerticalVelocity()
        dt[#dt+1] = "tas"
        dt[#dt+1] = LoGetTrueAirSpeed()
        dt[#dt+1] = "ias"
        dt[#dt+1] = LoGetIndicatedAirSpeed()
        dt[#dt+1] = "mach"
        dt[#dt+1] = LoGetMachNumber()
        dt[#dt+1] = "aoa"
        dt[#dt+1] = LoGetAngleOfAttack()
        dt[#dt+1] = "aoss"
        dt[#dt+1] = LoGetAngleOfSideSlip()
        dt[#dt+1] = "fuint"
        dt[#dt+1] = engine.fuel_internal
        dt[#dt+1] = "fuext"
        dt[#dt+1] = engine.fuel_external
        dt[#dt+1] = "surf"
        dt[#dt+1] = mech.controlsurfaces.eleron.left
        dt[#dt+1] = mech.controlsurfaces.eleron.right
        dt[#dt+1] = mech.controlsurfaces.elevator.left
        dt[#dt+1] = mech.controlsurfaces.elevator.right
        dt[#dt+1] = mech.controlsurfaces.rudder.left
        dt[#dt+1] = mech.controlsurfaces.rudder.right
        dt[#dt+1] = "flap"
        dt[#dt+1] = mech.flaps.value
        dt[#dt+1] = "airbrk"
        dt[#dt+1] = mech.speedbrakes.value
        dt[#dt+1] = "wind"
        local wind = LoGetVectorWindVelocity()
        dt[#dt+1] = wind.x
        dt[#dt+1] = wind.y
        dt[#dt+1] = wind.z
        dt[#dt+1] = "joyp"
        dt[#dt+1] = GetDevice(0):get_argument_value(71)
        dt[#dt+1] = "joyr"
        dt[#dt+1] = GetDevice(0):get_argument_value(74)
        dt[#dt+1] = "joyy"
        dt[#dt+1] = GetDevice(0):get_argument_value(500)
    end

    socket.try(UdpSocket:sendto(table.concat(dt,";"), "127.0.0.1", 9876))

    if socket.gettime() - LastBulkData > 1 then
        SendBulkData()
    end
end



function SendBulkData()
    -- see comments on dt[#dt+1] in LuaExportAfterNextFrame
    local dt = { }
    dt[#dt+1] = "bulk"
    dt[#dt+1] = "sess"
    dt[#dt+1] = Session
    local ownshipExport = LoIsOwnshipExportAllowed()
    dt[#dt+1] = "exp"
    dt[#dt+1] = ownshipExport and "true" or "false"
    if ownshipExport then
        local sdata = LoGetSelfData()
        dt[#dt+1] = "aircraft"
        dt[#dt+1] = sdata.Name
        --LoGetPayloadInfo()
    end

    socket.try(UdpSocket:sendto(table.concat(dt,";"), "127.0.0.1", 9876))
    LastBulkData = socket.gettime()
end



function LuaExportBeforeNextFrame()
    CurFrame = CurFrame + 1

    local received = UdpSocket:receive()
    if not received then
        Skips = Skips + 1
    else
        local data = { }
        local dataN = 0 -- Lua can't efficiently track table size by itself. It just can't. Because Lua.
        for val in string.gmatch(received, "([^;]*);") do
            dataN = dataN + 1
            data[dataN] = val
        end

        local i = 1
        while data[i] do
            local args = tonumber(data[i])
            i = i + 1
            local cmd = data[i]
            if cmd == "ts" then
                Latency = socket.gettime() - tonumber(data[i+1])
            elseif cmd == "sc" then
                if args == 1 then
                    LoSetCommand(tonumber(data[i+1]))
                elseif args == 2 then
                    LoSetCommand(tonumber(data[i+1]), tonumber(data[i+2]))
                end
            elseif cmd == "pca" then
                GetDevice(tonumber(data[i+1])):performClickableAction(tonumber(data[i+2]), tonumber(data[i+3]))
            elseif cmd == "pcaAR" then -- performClickableAction with auto-reset unless commanded again
                local dev = tonumber(data[i+1])
                local act = tonumber(data[i+2])
                GetDevice(dev):performClickableAction(act, tonumber(data[i+3]))
                AutoResetPCA[dev..";"..act] = { frame = CurFrame + 1, dev = dev, act = act, reset = tonumber(data[i+4]) }
            elseif cmd == "pca3w" then -- performClickableAction three-way switch that bugs out if both directions commanded simultaneously
                local dev = tonumber(data[i+1])
                local actN = tonumber(data[i+2])
                local actP = tonumber(data[i+3])
                local key = dev..";"..actN..";"..actP
                local val = tonumber(data[i+4])
                local cur = AutoResetPCA[key] and AutoResetPCA[key].cur or 0
                if cur == val then -- matches cur/val = 0/0, +/+, -/-
                    if cur ~= 0 then
                        AutoResetPCA[key].frame = CurFrame + 1
                    end
                elseif cur >= 0 and val >= 0 then -- matches cur/val = 0/+, +/0
                    GetDevice(dev):performClickableAction(actP, val)
                    --LogFile:write(LoGetModelTime() .. " ["..key.."] 1 GetDevice("..dev.."):pca("..actP..", "..val..")\n")
                    if val == 0 then
                        AutoResetPCA[key] = nil
                    else
                        AutoResetPCA[key] = { frame = CurFrame + 1, dev = dev, act = actP, reset = 0, cur = val }
                    end
                elseif cur <= 0 and val <= 0 then -- matches cur/val = 0/-, -/0
                    GetDevice(dev):performClickableAction(actN, math.abs(val))
                    --LogFile:write(LoGetModelTime() .. " ["..key.."] 2 GetDevice("..dev.."):pca("..actN..", "..math.abs(val)..")\n")
                    if val == 0 then
                        AutoResetPCA[key] = nil
                    else
                        AutoResetPCA[key] = { frame = CurFrame + 1, dev = dev, act = actN, reset = 0, cur = val }
                    end
                elseif cur > 0 then -- matches cur/val = +/-
                    GetDevice(dev):performClickableAction(actP, 0)
                    --LogFile:write(LoGetModelTime() .. " ["..key.."] 3 GetDevice("..dev.."):pca("..actP..", 0)\n")
                    AutoResetPCA[key] = nil
                elseif cur < 0 then -- matches cur/val = -/+
                    GetDevice(dev):performClickableAction(actN, 0)
                    --LogFile:write(LoGetModelTime() .. " ["..key.."] 4 GetDevice("..dev.."):pca("..actN..", 0)\n")
                    AutoResetPCA[key] = nil
                end
            end
            i = i + args + 1
        end
    end

    -- release buttons that need to be released
    for k, v in pairs(AutoResetPCA) do
        if v.frame <= CurFrame then
            GetDevice(v.dev):performClickableAction(v.act, v.reset)
            --LogFile:write(LoGetModelTime() .. " ["..k.."] 0 GetDevice("..v.dev.."):pca("..v.act..", "..v.reset..")\n")
            AutoResetPCA[k] = nil
        end
    end
end



function LogDiagnostics()
    LogFile:write("===== LoGetGlideDeviation() =====\n\n")
    LogFile:write(tableShow(LoGetGlideDeviation()).."\n\n")
    LogFile:write("===== LoGetSideDeviation() =====\n\n")
    LogFile:write(tableShow(LoGetSideDeviation()).."\n\n")
    LogFile:write("===== LoGetSlipBallPosition() =====\n\n")
    LogFile:write(tableShow(LoGetSlipBallPosition()).."\n\n")
    LogFile:write("===== LoGetMagneticYaw() =====\n\n")
    LogFile:write(tableShow(LoGetMagneticYaw()).."\n\n")
    
    LogFile:write("===== LoGetSelfData() =====\n\n")
    LogFile:write(tableShow(LoGetSelfData()).."\n\n")
    LogFile:write("===== LoGetEngineInfo() =====\n\n")
    LogFile:write(tableShow(LoGetEngineInfo()).."\n\n")
    LogFile:write("===== LoGetMechInfo() =====\n\n")
    LogFile:write(tableShow(LoGetMechInfo()).."\n\n")
    LogFile:write("===== LoGetPayloadInfo() =====\n\n")
    LogFile:write(tableShow(LoGetPayloadInfo()).."\n\n")
    LogFile:write("===== LoGetControlPanel_HSI() =====\n\n")
    LogFile:write(tableShow(LoGetControlPanel_HSI()).."\n\n")
    LogFile:write("===== LoGetADIPitchBankYaw() =====\n\n")
    LogFile:write(tableShow(LoGetADIPitchBankYaw()).."\n\n")
    LogFile:write("===== LoGetFMData() =====\n\n")
    LogFile:write(tableShow(LoGetFMData()).."\n\n")
    LogFile:write("===== LoGetBasicAtmospherePressure() =====\n\n")
    LogFile:write(tableShow(LoGetBasicAtmospherePressure()).."\n\n")
    LogFile:write("===== LoGetMCPState() =====\n\n")
    LogFile:write(tableShow(LoGetMCPState()).."\n\n")
    LogFile:write("===== LoGetRoute() =====\n\n")
    LogFile:write(tableShow(LoGetRoute()).."\n\n")
    LogFile:write("===== LoGetNavigationInfo() =====\n\n")
    LogFile:write(tableShow(LoGetNavigationInfo()).."\n\n")
    LogFile:write("===== LoGetWingInfo() =====\n\n")
    LogFile:write(tableShow(LoGetWingInfo()).."\n\n")
    LogFile:write("===== LoGetRadioBeaconsStatus() =====\n\n")
    LogFile:write(tableShow(LoGetRadioBeaconsStatus()).."\n\n")
    LogFile:write("===== LoGetSnares() =====\n\n")
    LogFile:write(tableShow(LoGetSnares()).."\n\n")
    --LoGetHeightWithObjects

    -- Available if IsSensorExportAllowed
    LogFile:write("===== LoGetTWSInfo() =====\n\n")
    LogFile:write(tableShow(LoGetTWSInfo()).."\n\n")
    LogFile:write("===== LoGetTargetInformation() =====\n\n")
    LogFile:write(tableShow(LoGetTargetInformation()).."\n\n")
    LogFile:write("===== LoGetLockedTargetInformation() =====\n\n")
    LogFile:write(tableShow(LoGetLockedTargetInformation()).."\n\n")
    --Export.LoGetF15_TWS_Contacts
    LogFile:write("===== LoGetSightingSystemInfo() =====\n\n")
    LogFile:write(tableShow(LoGetSightingSystemInfo()).."\n\n")
    LogFile:write("===== LoGetWingTargets() =====\n\n")
    LogFile:write(tableShow(LoGetWingTargets()).."\n\n")

    LogFile:write("===== _G =====\n\n")
    LogFile:write(tableShow(_G).."\n\n")
end



function basicSerialize(var) -- FROM SRS
    if var == nil then
        return "\"\""
    else
        if ((type(var) == 'number') or
                (type(var) == 'boolean') or
                (type(var) == 'function') or
                (type(var) == 'table') or
                (type(var) == 'userdata') ) then
            return tostring(var)
        elseif type(var) == 'string' then
            var = string.format('%q', var)
            return var
        end
    end
end



function tableShow(tbl, loc, indent, tableshow_tbls) -- FROM SRS --based on serialize_slmod, this is a _G serialization
    tableshow_tbls = tableshow_tbls or {} --create table of tables
    loc = loc or ""
    indent = indent or ""
    if type(tbl) == 'table' then --function only works for tables!
        tableshow_tbls[tbl] = loc

        local tbl_str = {}

        tbl_str[#tbl_str + 1] = indent .. '{\n'

        for ind,val in pairs(tbl) do -- serialize its fields
            if type(ind) == "number" then
                tbl_str[#tbl_str + 1] = indent
                tbl_str[#tbl_str + 1] = loc .. '['
                tbl_str[#tbl_str + 1] = tostring(ind)
                tbl_str[#tbl_str + 1] = '] = '
            else
                tbl_str[#tbl_str + 1] = indent
                tbl_str[#tbl_str + 1] = loc .. '['
                tbl_str[#tbl_str + 1] = basicSerialize(ind)
                tbl_str[#tbl_str + 1] = '] = '
            end

            if ((type(val) == 'number') or (type(val) == 'boolean')) then
                tbl_str[#tbl_str + 1] = tostring(val)
                tbl_str[#tbl_str + 1] = ',\n'
            elseif type(val) == 'string' then
                tbl_str[#tbl_str + 1] = basicSerialize(val)
                tbl_str[#tbl_str + 1] = ',\n'
            elseif type(val) == 'nil' then -- won't ever happen, right?
                tbl_str[#tbl_str + 1] = 'nil,\n'
            elseif type(val) == 'table' then
                if tableshow_tbls[val] then
                    tbl_str[#tbl_str + 1] = tostring(val) .. ' already defined: ' .. tableshow_tbls[val] .. ',\n'
                else
                    tableshow_tbls[val] = loc ..    '[' .. basicSerialize(ind) .. ']'
                    tbl_str[#tbl_str + 1] = tostring(val) .. ' '
                    tbl_str[#tbl_str + 1] = tableShow(val,  loc .. '[' .. basicSerialize(ind).. ']', indent .. '        ', tableshow_tbls)
                    tbl_str[#tbl_str + 1] = ',\n'
                end
            elseif type(val) == 'function' then
                if debug and debug.getinfo then
                    local fcnname = tostring(val)
                    local info = debug.getinfo(val, "S")
                    if info.what == "C" then
                        tbl_str[#tbl_str + 1] = string.format('%q', fcnname .. ', C function') .. ',\n'
                    else
                        if (string.sub(info.source, 1, 2) == [[./]]) then
                            tbl_str[#tbl_str + 1] = string.format('%q', fcnname .. ', defined in (' .. info.linedefined .. '-' .. info.lastlinedefined .. ')' .. info.source) ..',\n'
                        else
                            tbl_str[#tbl_str + 1] = string.format('%q', fcnname .. ', defined in (' .. info.linedefined .. '-' .. info.lastlinedefined .. ')') ..',\n'
                        end
                    end

                else
                    tbl_str[#tbl_str + 1] = 'a function,\n'
                end
            elseif type(val) == 'userdata' then
                tbl_str[#tbl_str + 1] = 'userdata '
                tbl_str[#tbl_str + 1] = tableShow(getmetatable(val),  loc .. '[' .. basicSerialize(ind).. ']', indent .. '        ', tableshow_tbls)
                tbl_str[#tbl_str + 1] = ',\n'
            else
                tbl_str[#tbl_str + 1] = 'unable to serialize value type ' .. basicSerialize(type(val)) .. ' at index ' .. tostring(ind)
            end
        end

        tbl_str[#tbl_str + 1] = indent .. '}'
        return table.concat(tbl_str)
    elseif tbl == nil then
        return "nil"
    else
        return basicSerialize(tbl)
    end
end
