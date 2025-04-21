{{ config(materialized='view') }}

select
  f.post_id,
  f.score,
  f.num_comments,
  f.engagement,
  a.author_id,
  a.first_seen_utc,
  t.timestamp_hour,
  t.date,
  t.hour_of_day,
  t.day_of_week,
  t.is_weekend
from {{ ref('fact_posts') }} f
join {{ ref('dim_author') }} a
  on f.author_id = a.author_id
join {{ ref('dim_time')   }} t
  on f.time_id = t.time_id
