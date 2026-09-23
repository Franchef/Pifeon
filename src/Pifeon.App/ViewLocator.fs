namespace Pifeon.App

open System
open Avalonia.Controls
open Avalonia.Controls.Templates
open Pifeon.App.ViewModels

type ViewLocator() =
    interface IDataTemplate with
        
        member this.Build(data: obj) =
            match data with
            | null -> upcast TextBlock(Text = "Data is null")
            | _ ->
                let dataType = data.GetType()
                let fullName = dataType.FullName
                if isNull fullName then
                    upcast TextBlock(Text = "Type name is null")
                else
                    let name = fullName.Replace("ViewModel", "View", StringComparison.Ordinal)
                    let typ = Type.GetType(name)
                    if isNull typ then
                        upcast TextBlock(Text = sprintf "Not Found: %s" name)
                    else
                        let instance = Activator.CreateInstance(typ)
                        match instance with
                        | :? Control as control -> control
                        | _ -> upcast TextBlock(Text = sprintf "Failed to create control for: %s" name)
                
        member this.Match(data: obj) =
            match data with
            | null -> false
            | _ -> data :? ViewModelBase