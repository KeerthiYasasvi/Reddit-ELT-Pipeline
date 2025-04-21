{{ config(
    materialized='table',
    unique_key='post_id'
) }}

with posts as (

    select
      post_id,
      author_id,
      created_at,
      score,
      num_comments,
      (score + num_comments) as engagement
    from {{ ref('stg_posts') }}

),

joined as (

    select
      p.post_id,
      p.score,
      p.num_comments,
      p.engagement,
      a.author_id          as author_id,
      t.time_id            as time_id
    from posts p

    -- join to author dimension
    join {{ ref('dim_author') }} a
      on p.author_id = a.author_id

    -- join to time dimension (truncate to hour for matching)
    join {{ ref('dim_time') }} t
      on date_trunc('hour', p.created_at) = t.timestamp_hour

)

select * from joined
