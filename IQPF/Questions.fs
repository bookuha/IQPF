module IQPF.Questions
open System

[<CLIMutable>]
type Question = {
    id: Guid option
    title: string
    description: string
    added: DateTime option
}