local socket = require("socket")
local lfs = require('lfs')
local UdpSocket = nil
local LogFile = nil
local Skips = 0
local Session = socket.gettime()
local Latency = 0
local LastBulkData = 0



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

    -- for advanced AP functions
    -- LoGetGlideDeviation
    -- LoGetLockedTargetInformation
    -- more functions to explore:
    -- GetDevice
    -- GetIndicator
    -- LoGetAircraftDrawArgumentValue
    -- LoGetBasicAtmospherePressure
    -- LoGetADIPitchBankYaw
    -- LoGetBasicAtmospherePressure
    -- LoGetControlPanel_HSI
    -- LoGetFMData
    
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
    local received = UdpSocket:receive()
    if not received then
        Skips = Skips + 1
        return
    end

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
        end
        i = i + args + 1
    end
end



function LogDiagnostics()
    LogFile:write("===== LoGetSelfData() =====\n\n")
    LogFile:write(tableShow(LoGetSelfData()).."\n\n")
    LogFile:write("===== LoGetEngineInfo() =====\n\n")
    LogFile:write(tableShow(LoGetEngineInfo()).."\n\n")
    LogFile:write("===== LoGetMechInfo() =====\n\n")
    LogFile:write(tableShow(LoGetMechInfo()).."\n\n")
    LogFile:write("===== LoGetPayloadInfo() =====\n\n")
    LogFile:write(tableShow(LoGetPayloadInfo()).."\n\n")

    LogFile:write("===== LoGetVectorVelocity() =====\n\n")
    LogFile:write(tableShow(LoGetVectorVelocity()).."\n\n")
    LogFile:write("===== LoGetAngularVelocity() =====\n\n")
    LogFile:write(tableShow(LoGetAngularVelocity()).."\n\n")
    LogFile:write("===== LoGetVectorWindVelocity() =====\n\n")
    LogFile:write(tableShow(LoGetVectorWindVelocity()).."\n\n")

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
            else
                tbl_str[#tbl_str + 1] = 'unable to serialize value type ' .. basicSerialize(type(val)) .. ' at index ' .. tostring(ind)
            end
        end

        tbl_str[#tbl_str + 1] = indent .. '}'
        return table.concat(tbl_str)
    end
end
