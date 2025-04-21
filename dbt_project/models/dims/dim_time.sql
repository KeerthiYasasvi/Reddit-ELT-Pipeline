{{ config(
    materialized='table',
    unique_key='time_id'
) }}

with unique_timestamps as (

    select distinct
      date_trunc('hour', created_at) as ts_hour
    from {{ ref('stg_posts') }}

),

numbered as (

    select
      row_number() over (order by ts_hour) as time_id,
      ts_hour
    from unique_timestamps

)

select
  time_id,
  ts_hour                                    as timestamp_hour,
  date(ts_hour)                              as date,
  extract(hour from ts_hour)                 as hour_of_day,
  to_char(ts_hour, 'Day')                    as day_of_week,
  (extract(isodow from ts_hour) in (6,7))    as is_weekend
from numbered
order by ts_hour
