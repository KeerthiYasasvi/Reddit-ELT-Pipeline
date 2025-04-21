{{ config(materialized='table') }}

with stats as (

  select
    f.author_id,
    count(*)              as total_posts,
    round(avg(f.score), 2)          as avg_score,
    round(avg(f.num_comments), 2)   as avg_comments,
    sum(f.engagement)     as total_engagement

  from {{ ref('fact_posts') }} f

  group by f.author_id

)

select
  s.*,
  a.first_seen_utc

from stats s

-- bring in author details
join {{ ref('dim_author') }} a
  on s.author_id = a.author_id
