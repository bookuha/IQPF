module IQPF.Database

open System
open System.Threading.Tasks
open IQPF.Questions
open Npgsql
open Npgsql.FSharp

type SqlOrder =
    | Ascending = 0
    | Descending = 1
    
[<RequireQualifiedAccess>]    
type UpdateDbResult<'a> =
    | Success of 'a
    | NoDataFound
    | UniqueViolation
    | Error      

[<RequireQualifiedAccess>]        
type InsertDbResult<'a> =
    | Success of 'a
    | UniqueViolation
    | Error

[<RequireQualifiedAccess>]             
type DeleteDbResult =
    | Success
    | NoDataFound
    | Error

[<RequireQualifiedAccess>]        
type FetchDbResult<'a> =
    | Success of 'a
    | Error    

[<AbstractClass; Sealed>]   
type QuestionRepo private () =
    static let conn =
        Sql.host (Environment.GetEnvironmentVariable "POSTGRES_HOST")
        |> Sql.database (Environment.GetEnvironmentVariable "POSTGRES_DATABASE")
        |> Sql.username (Environment.GetEnvironmentVariable "POSTGRES_USER")
        |> Sql.password (Environment.GetEnvironmentVariable "POSTGRES_PASSWORD")
        |> Sql.trustServerCertificate true
        |> Sql.formatConnectionString
        |> (+) "Pooling=false;"
             
    static member getQuestions (?count: int, ?order: SqlOrder) : FetchDbResult<Task<Question list>> =
            
        let order = int (defaultArg (order) (SqlOrder.Ascending))    
        
        try
            conn
            |> Sql.connect
            |> Sql.query "SELECT *
                          FROM questions
                          ORDER BY CASE WHEN @order = '1' THEN added END DESC,
                          added
                          LIMIT CASE WHEN @count::INT IS NOT NULL THEN @count::INT ELSE 1000000 END;"
            |> Sql.parameters ["order", Sql.int order; "count", Sql.intOrNone count]
            |> Sql.executeAsync (fun read ->    
                {
                    id = read.uuidOrNone "question_id"
                    title = read.string "title"
                    description = read.string "description"
                    added = read.dateTimeOrNone "added"         
                }
            )
            |> FetchDbResult.Success
        with
            | _ -> FetchDbResult.Error
        
    
    static member insertQuestion (question:Question) : InsertDbResult<Task<Question list>> =      
        try
            conn
            |> Sql.connect
            |> Sql.query "INSERT INTO questions
                          (title, description)
                          VALUES (@title, @description)
                          RETURNING *"
            |> Sql.parameters ["title", Sql.string question.title; "description", Sql.string question.description]
            |> Sql.executeAsync (fun read ->
                {
                    id = read.uuidOrNone "question_id"
                    title = read.string "title"
                    description = read.string "description"
                    added = read.dateTimeOrNone "added"
                }
            )
            |> InsertDbResult.Success
        with
           | :? PostgresException as ex when ex.ErrorCode = 23505 -> InsertDbResult.UniqueViolation
           | _ -> InsertDbResult.Error

        
        
    static member updateQuestionById (id: Guid) (question: Question) : UpdateDbResult<Task<Question list>> =
        try
            conn
            |> Sql.connect
            |> Sql.query "UPDATE questions
                          SET title = @title,
                          description = @description
                          WHERE question_id = @id
                          RETURNING *"
            |> Sql.parameters
                [ "title", Sql.string question.title
                  "description", Sql.string question.description
                  "id", Sql.uuid id ]
            |> Sql.executeAsync (fun read ->
                { id = read.uuidOrNone "question_id"
                  title = read.string "title"
                  description = read.string "description"
                  added = read.dateTimeOrNone "added" })
            |> UpdateDbResult.Success
        with
            | :? PostgresException as ex when ex.ErrorCode = 23505 -> UpdateDbResult.UniqueViolation
            | :? PostgresException as ex when ex.ErrorCode = 02000 -> UpdateDbResult.NoDataFound
            | _ -> UpdateDbResult.Error
        
        
        
        
    static member deleteQuestionById (id: Guid) : Task<DeleteDbResult> =
        try
            task {
                let! deleted =
                    conn
                    |> Sql.connect
                    |> Sql.query "DELETE FROM questions
                                  WHERE question_id = @id"
                    |> Sql.parameters [ "id", Sql.uuid id ]
                    |> Sql.executeNonQueryAsync
                
                return match deleted with
                        | 1 -> DeleteDbResult.Success
                        | 0 -> DeleteDbResult.NoDataFound
                        | _ -> DeleteDbResult.Error    
           }
        with
            | :? PostgresException as ex when ex.ErrorCode = 02000 -> Task.FromResult DeleteDbResult.NoDataFound
            | _ -> Task.FromResult DeleteDbResult.Error