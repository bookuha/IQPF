CREATE TABLE questions (
    question_id uuid DEFAULT uuid_generate_v4(),
    title VARCHAR NOT NULL,
    description VARCHAR NOT NULL,
    added TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (question_id)
);