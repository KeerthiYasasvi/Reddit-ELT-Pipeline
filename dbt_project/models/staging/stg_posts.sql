{{ config(materialized='table') }}

-- Staging model for raw_posts
WITH raw AS (
    SELECT
        id,
        title,
        author,
        num_comments,
        upvote_ratio,
        score,
        created_utc,
        over_18,
        url,
        subreddit
    FROM {{ source('reddit_pipeline', 'raw_posts') }}
)

SELECT
    id             AS post_id,
    title,
    author         AS author_id,
    num_comments,
    upvote_ratio,
    score,
    (score + num_comments) AS engagement,
    created_utc    AS created_at,
    over_18,
    url,
    subreddit
FROM raw
