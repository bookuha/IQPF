module IQPF.App
open System
open System.Reflection
open DbUp
open IQPF.Database
open IQPF.Questions
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection
open Giraffe
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting
open Npgsql.FSharp

let mutable inMemoryDb: Question array = [||]

let getQuestionsHandler: HttpHandler =
     fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            
        let result = QuestionRepo.getQuestions()
        
        match result with
            | FetchDbResult.Success task ->
                let! questions = task
                let response = questions |> json
                return! Successful.ok(response) next ctx
            | FetchDbResult.Error -> return! RequestErrors.conflict ("Server error" |> json) next ctx
        }
    

type PostQuestionDTO = { title: string; description: string }

let bindDtoToQuestion dto =
    {id = None; title = dto.title; description = dto.description; added = None }
    
let createQuestionHandler: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! questionDto = ctx.BindJsonAsync<PostQuestionDTO>()

            let question = bindDtoToQuestion questionDto
           
            let result = QuestionRepo.insertQuestion question
            
            match result with
            | InsertDbResult.Success task ->
                let! question = task
                let response = question |> json
                return! Successful.ok(response) next ctx
            | InsertDbResult.UniqueViolation -> return! RequestErrors.conflict ("Record with such id already exists" |> json) next ctx
            | InsertDbResult.Error -> return! ServerErrors.internalError ("Database error" |> json) next ctx
        }
   
let webApp =
    choose [
        route "/questions" >=> choose [
            GET >=> warbler (fun _ -> getQuestionsHandler)
            POST >=> warbler (fun _ -> createQuestionHandler)
        ]
        RequestErrors.NOT_FOUND "Not Found"
    ]

let configureApp (app : IApplicationBuilder) =
    
    let connectionString =
        Sql.host (Environment.GetEnvironmentVariable "POSTGRES_HOST")
        |> Sql.database (Environment.GetEnvironmentVariable "POSTGRES_DATABASE")
        |> Sql.username (Environment.GetEnvironmentVariable "POSTGRES_USER")
        |> Sql.password (Environment.GetEnvironmentVariable "POSTGRES_PASSWORD")
        |> Sql.trustServerCertificate true
        |> Sql.formatConnectionString
        |> (+) "Pooling=false;"
        
    EnsureDatabase.For.PostgresqlDatabase(connectionString);    
        
    let upgrader =
        DeployChanges.To
            .PostgresqlDatabase(connectionString)
            .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly())
            .LogToConsole()
            .Build()
            
    let result = upgrader.PerformUpgrade()
    
    let info =
        match result.Successful with
        | true -> "Success!"
        | false -> result.Error.Message
        
    Console.WriteLine(info)
    
    // Add Giraffe to the ASP.NET Core pipeline
    app.UseGiraffe webApp

let configureServices (services : IServiceCollection) =
    // Add Giraffe dependencies
    services.AddGiraffe() |> ignore
    
    let serializationOptions = SystemTextJson.Serializer.DefaultOptions
    services.AddSingleton<Json.ISerializer>(SystemTextJson.Serializer(serializationOptions)) |> ignore

[<EntryPoint>]
let main _ =
    Host.CreateDefaultBuilder()
        .ConfigureWebHostDefaults(
            fun webHostBuilder ->
                webHostBuilder
                    .Configure(configureApp)
                    .ConfigureServices(configureServices)
                    |> ignore)
        .Build()
        .Run()
    0