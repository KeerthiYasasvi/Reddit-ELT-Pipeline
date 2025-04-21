{{ config(
    materialized='table',
    unique_key='author_id'
) }}

with authors as (

    select
      author_id,
      min(created_at)   as first_seen_utc
    from {{ ref('stg_posts') }}
    group by author_id

)

select
  author_id,
  first_seen_utc
from authors
